using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Linq;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshApiExposureAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0013,
        UshRuleDescriptors.Ush0014,
        UshRuleDescriptors.Ush0015);

    private static readonly ImmutableArray<string> ForbiddenNamespacePrefixes = ImmutableArray.Create(
        "System.IO",
        "System.Net",
        "System.Reflection",
        "System.Threading",
        "System.Diagnostics",
        "System.Security",
        "System.Runtime.InteropServices",
        "System.Web",
        "UnityEditor");

    private static readonly ImmutableArray<string> ForbiddenTypeNames = ImmutableArray.Create(
        "global::System.IntPtr",
        "global::System.UIntPtr",
        "global::System.Diagnostics.Process",
        "global::System.Random",
        "global::System.Net.Http.HttpClient",
        "global::System.Net.WebClient",
        "global::System.Threading.Tasks.Task");

    private static readonly ImmutableArray<string> ForbiddenMemberNames = ImmutableArray.Create(
        "global::UnityEngine.Component.gameObject",
        "global::UnityEngine.GameObject.gameObject");

    private static readonly ImmutableHashSet<string> ForbiddenMemberNamesNormalized = ForbiddenMemberNames
        .Select(NormalizeQualifiedName)
        .ToImmutableHashSet(StringComparer.Ordinal);

    private static readonly ImmutableHashSet<string> ForbiddenTypeNamesNormalized = ForbiddenTypeNames
        .Select(NormalizeQualifiedName)
        .ToImmutableHashSet(StringComparer.Ordinal);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var methodSymbols = GetMethodCandidates(symbolInfo);
        if (methodSymbols.Length == 0)
        {
            var syntaxMethodName = GetInvocationMethodName(invocation.Expression);
            if (IsForbiddenGetComponentName(syntaxMethodName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0013,
                    invocation.GetLocation(),
                    syntaxMethodName ?? "GetComponent"));
                return;
            }

            var qualifiedExpression = GetQualifiedExpressionName(invocation.Expression);
            if (MatchesForbiddenNamespace(qualifiedExpression))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0013,
                    invocation.GetLocation(),
                    syntaxMethodName ?? qualifiedExpression ?? invocation.ToString()));
                return;
            }

            return;
        }

        foreach (var method in methodSymbols)
        {
            if (IsForbiddenGetComponent(method))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0013,
                    invocation.GetLocation(),
                    method.Name));
                return;
            }

            if (IsForbiddenNamespace(method.ContainingNamespace))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0013,
                    invocation.GetLocation(),
                    method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return;
            }
        }
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken);
        var symbols = GetSymbolCandidates(symbolInfo);
        if (symbols.Length == 0)
        {
            var memberText = GetQualifiedExpressionName(access);
            if (IsForbiddenMemberName(memberText))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0014,
                    access.Name.GetLocation(),
                    access.Name.Identifier.Text));
                return;
            }

            if (MatchesForbiddenNamespace(memberText))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0014,
                    access.Name.GetLocation(),
                    access.Name.Identifier.Text));
            }
            return;
        }

        foreach (var symbol in symbols)
        {
            if (symbol is not (IPropertySymbol or IFieldSymbol))
            {
                continue;
            }

            var memberName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (IsForbiddenMemberName(memberName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0014,
                    access.Name.GetLocation(),
                    symbol.Name));
                return;
            }

            if (IsForbiddenNamespace(symbol.ContainingType?.ContainingNamespace))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0014,
                    access.Name.GetLocation(),
                    symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return;
            }
        }
    }

    private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (VariableDeclarationSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(declaration.Type, context.CancellationToken);
        var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        if (typeSymbol is null)
        {
            ReportForbiddenTypeByText(context, declaration.Type.ToString(), declaration.Type.GetLocation());
            return;
        }

        ReportForbiddenType(context, typeSymbol, declaration.Type.GetLocation());
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (FieldDeclarationSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(declaration.Declaration.Type, context.CancellationToken);
        var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        if (typeSymbol is null)
        {
            ReportForbiddenTypeByText(context, declaration.Declaration.Type.ToString(), declaration.Declaration.Type.GetLocation());
            return;
        }

        ReportForbiddenType(context, typeSymbol, declaration.Declaration.Type.GetLocation());
    }

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(declaration.Type, context.CancellationToken);
        var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        if (typeSymbol is null)
        {
            ReportForbiddenTypeByText(context, declaration.Type.ToString(), declaration.Type.GetLocation());
            return;
        }

        ReportForbiddenType(context, typeSymbol, declaration.Type.GetLocation());
    }

    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (parameter.Type is null)
        {
            return;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken);
        var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        if (typeSymbol is null)
        {
            ReportForbiddenTypeByText(context, parameter.Type.ToString(), parameter.Type.GetLocation());
            return;
        }

        ReportForbiddenType(context, typeSymbol, parameter.Type.GetLocation());
    }

    private static bool IsForbiddenGetComponent(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        if (!string.Equals(definition.Name, "GetComponent", StringComparison.Ordinal) &&
            !string.Equals(definition.Name, "GetComponents", StringComparison.Ordinal))
        {
            return false;
        }

        var containingType = definition.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(containingType, "global::UnityEngine.Component", StringComparison.Ordinal) ||
               string.Equals(containingType, "global::UnityEngine.GameObject", StringComparison.Ordinal);
    }

    private static void ReportForbiddenType(SyntaxNodeAnalysisContext context, ITypeSymbol typeSymbol, Location location)
    {
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            string.Equals(namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Nullable<T>", StringComparison.Ordinal))
        {
            typeSymbol = namedType.TypeArguments[0];
        }

        var fullyQualified = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (ForbiddenTypeNames.Contains(fullyQualified))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0015,
                location,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return;
        }

        if (IsForbiddenNamespace(typeSymbol.ContainingNamespace))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0015,
                location,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static void ReportForbiddenTypeByText(SyntaxNodeAnalysisContext context, string typeNameText, Location location)
    {
        foreach (var candidate in EnumerateTypeNameCandidates(typeNameText))
        {
            if (!MatchesForbiddenType(candidate))
            {
                continue;
            }

            var normalized = NormalizeQualifiedName(candidate);
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0015,
                location,
                string.IsNullOrEmpty(normalized) ? candidate : normalized));
            break;
        }
    }

    private static IEnumerable<string> EnumerateTypeNameCandidates(string typeNameText)
    {
        var trimmed = typeNameText?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            yield break;
        }

        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            yield break;
        }

        yield return trimmed;

        var prefixes = new[]
        {
            "System.Nullable<",
            "global::System.Nullable<"
        };

        foreach (var prefix in prefixes)
        {
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) || !trimmed.EndsWith(">", StringComparison.Ordinal))
            {
                continue;
            }

            var inner = trimmed.Substring(prefix.Length, trimmed.Length - prefix.Length - 1).Trim();
            if (!string.IsNullOrEmpty(inner))
            {
                yield return inner;
            }
        }
    }

    private static bool MatchesForbiddenType(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = NormalizeQualifiedName(candidate);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (ForbiddenTypeNamesNormalized.Contains(normalized))
        {
            return true;
        }

        var globalName = $"global::{normalized}";
        if (ForbiddenTypeNames.Contains(globalName))
        {
            return true;
        }

        return MatchesForbiddenNamespace(normalized);
    }

    private static bool IsForbiddenMemberName(string? memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        var normalized = NormalizeQualifiedName(memberName);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return ForbiddenMemberNamesNormalized.Contains(normalized);
    }

    private static bool IsForbiddenGetComponentName(string? methodName)
    {
        return string.Equals(methodName, "GetComponent", StringComparison.Ordinal) ||
               string.Equals(methodName, "GetComponents", StringComparison.Ordinal);
    }

    private static string? GetInvocationMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => GetSimpleName(memberAccess.Name),
            MemberBindingExpressionSyntax memberBinding => GetSimpleName(memberBinding.Name),
            ConditionalAccessExpressionSyntax conditional => GetInvocationMethodName(conditional.WhenNotNull),
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => null,
        };
    }

    private static string? GetQualifiedExpressionName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => CombineQualified(
                GetQualifiedExpressionName(memberAccess.Expression),
                GetSimpleName(memberAccess.Name)),
            ConditionalAccessExpressionSyntax conditional => GetQualifiedExpressionName(conditional.WhenNotNull),
            MemberBindingExpressionSyntax memberBinding => GetSimpleName(memberBinding.Name),
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => CombineQualified(
                GetQualifiedExpressionName(qualified.Left),
                qualified.Right.Identifier.Text),
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            InvocationExpressionSyntax invocation => GetQualifiedExpressionName(invocation.Expression),
            _ => expression.ToString().Trim()
        };
    }

    private static string CombineQualified(string? left, string right)
    {
        if (string.IsNullOrEmpty(right))
        {
            return left ?? string.Empty;
        }

        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        return $"{left}.{right}";
    }

    private static string GetSimpleName(SimpleNameSyntax nameSyntax)
    {
        return nameSyntax.Identifier.Text;
    }

    private static string NormalizeQualifiedName(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return string.Empty;
        }

        var normalized = qualifiedName.Trim();

        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized.Substring("global::".Length);
        }

        var aliasSeparator = normalized.IndexOf("::", StringComparison.Ordinal);
        if (aliasSeparator >= 0)
        {
            normalized = normalized.Substring(aliasSeparator + 2);
        }

        var genericIndex = normalized.IndexOf('<');
        if (genericIndex >= 0)
        {
            normalized = normalized.Substring(0, genericIndex);
        }

        while (normalized.EndsWith("[]", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(0, normalized.Length - 2);
        }

        return normalized;
    }

    private static bool MatchesForbiddenNamespace(string? qualifiedName)
    {
        var normalized = NormalizeQualifiedName(qualifiedName);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        foreach (var prefix in ForbiddenNamespacePrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForbiddenNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToDisplayString();
        if (string.IsNullOrEmpty(namespaceName))
        {
            return false;
        }

        foreach (var prefix in ForbiddenNamespacePrefixes)
        {
            if (namespaceName!.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<IMethodSymbol> GetMethodCandidates(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return ImmutableArray.Create(method);
        }

        return symbolInfo.CandidateSymbols
            .OfType<IMethodSymbol>()
            .ToImmutableArray();
    }

    private static ImmutableArray<ISymbol> GetSymbolCandidates(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol is not null)
        {
            return ImmutableArray.Create(symbolInfo.Symbol);
        }

        return symbolInfo.CandidateSymbols;
    }
}
