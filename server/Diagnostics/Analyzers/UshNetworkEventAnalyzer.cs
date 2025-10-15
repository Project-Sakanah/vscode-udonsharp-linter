using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshNetworkEventAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0001,
        UshRuleDescriptors.Ush0002,
        UshRuleDescriptors.Ush0003,
        UshRuleDescriptors.Ush0004,
        UshRuleDescriptors.Ush0005,
        UshRuleDescriptors.Ush0006,
        UshRuleDescriptors.Ush0043);

    private static readonly HashSet<string> CustomEventMethodNames = new(StringComparer.Ordinal)
    {
        "SendCustomEvent",
        "SendCustomEventDelayedFrames",
        "SendCustomEventDelayedSeconds"
    };

    private static readonly HashSet<string> NetworkEventMethodNames = new(StringComparer.Ordinal)
    {
        "SendCustomNetworkEvent",
        "SendCustomNetworkEventDelayedFrames",
        "SendCustomNetworkEventDelayedSeconds"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!TryIdentifyEventInvocation(invocation, context.SemanticModel, context.CancellationToken, out var methodSymbol, out var methodName, out var isCustomEvent, out var isNetworkEvent))
        {
            return;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            return;
        }

        var eventArgumentIndex = isNetworkEvent ? 1 : 0;
        if (arguments.Count <= eventArgumentIndex)
        {
            return;
        }

        var eventArgument = arguments[eventArgumentIndex];
        if (!UshAnalyzerUtilities.TryResolveEventTarget(context.SemanticModel, eventArgument.Expression, context.CancellationToken, out var targetName, out var resolvedMethodSymbol))
        {
            if (UshAnalyzerUtilities.IsStringLiteral(eventArgument.Expression))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0043,
                    eventArgument.GetLocation(),
                    eventArgument.Expression.ToString().Trim('"')));
            }

            var fallbackMethodName = ExtractMethodNameFromTarget(context, eventArgument.Expression);
            if (TryAnalyzeEventsSyntaxOnly(context, invocation, eventArgument, fallbackMethodName ?? string.Empty, isNetworkEvent))
            {
                return;
            }

            return;
        }

        var targetBehaviour = DetermineTargetBehaviour(invocation, methodSymbol, resolvedMethodSymbol, context.SemanticModel, context.CancellationToken);
        if (targetBehaviour is null || !UshAnalyzerUtilities.IsUdonSharpBehaviour(targetBehaviour))
        {
            if (TryAnalyzeEventsSyntaxOnly(context, invocation, eventArgument, targetName, isNetworkEvent))
            {
                if (UshAnalyzerUtilities.IsStringLiteral(eventArgument.Expression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UshRuleDescriptors.Ush0043,
                        eventArgument.GetLocation(),
                        targetName));
                }
            }

            return;
        }

        if (resolvedMethodSymbol?.ContainingType is INamedTypeSymbol resolvedContaining &&
            UshAnalyzerUtilities.IsUdonSharpBehaviour(resolvedContaining))
        {
            targetBehaviour = resolvedContaining;
        }

        var methodSet = new HashSet<IMethodSymbol>(MethodSymbolComparer.Instance);
        foreach (var method in UshAnalyzerUtilities.GetBehaviourMethods(targetBehaviour, targetName))
        {
            methodSet.Add(method);
        }

        if (resolvedMethodSymbol is not null &&
            resolvedMethodSymbol.ContainingType?.Equals(targetBehaviour, SymbolEqualityComparer.Default) == true)
        {
            methodSet.Add(resolvedMethodSymbol);
        }

        var candidateMethods = ImmutableArray.CreateRange(methodSet);

        ValidateTargetExistence(context, eventArgument, targetName, candidateMethods);
        ValidateTargetAccessibility(context, eventArgument, targetName, candidateMethods);

        if (isNetworkEvent)
        {
            ValidateNetworkNaming(context, eventArgument, targetName);
            ValidateNetworkCallableAttribute(context, arguments, eventArgument, candidateMethods, targetName, context.CancellationToken);
            ValidateNetworkParameterTypes(context, invocation, arguments, candidateMethods, targetName);
            ValidateNetworkSyncMode(context, invocation, targetBehaviour, context.SemanticModel, context.CancellationToken);
        }

        if (UshAnalyzerUtilities.IsStringLiteral(eventArgument.Expression))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0043,
                eventArgument.GetLocation(),
                targetName));
        }
    }

    private readonly struct SyntaxMethodCandidate
    {
        public SyntaxMethodCandidate(string name, bool isPublic, bool hasNetworkCallable, ImmutableArray<string> parameterTypes)
        {
            Name = name;
            IsPublic = isPublic;
            HasNetworkCallable = hasNetworkCallable;
            ParameterTypes = parameterTypes;
        }

        public string Name { get; }
        public bool IsPublic { get; }
        public bool HasNetworkCallable { get; }
        public ImmutableArray<string> ParameterTypes { get; }
    }

    private static bool TryAnalyzeEventsSyntaxOnly(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax eventArgument,
        string methodName,
        bool isNetworkEvent)
    {
        if (string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        var targetType = TryExtractDeclaringTypeFromNameof(eventArgument.Expression) is { } typeFromNameof
            ? UshAnalyzerUtilities.FindTypeDeclarationInSameDocument(invocation, typeFromNameof)
            : null;

        if (targetType is null && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var leftMostIdentifier = GetLeftMostIdentifier(memberAccess.Expression);
            if (!string.IsNullOrEmpty(leftMostIdentifier))
            {
                var typeName = UshAnalyzerUtilities.FindTypeNameOfIdentifierInSameDocument(invocation, leftMostIdentifier);
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    var simple = ExtractSimpleTypeName(typeName!);
                    targetType = UshAnalyzerUtilities.FindTypeDeclarationInSameDocument(invocation, simple);
                }
            }
        }

        targetType ??= invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (targetType is null)
        {
            return false;
        }

        var candidates = FindCandidateMethodsBySyntax(targetType, methodName).ToImmutableArray();

        if (candidates.IsDefaultOrEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0001,
                eventArgument.GetLocation(),
                methodName));
        }
        else if (!candidates.Any(candidate => candidate.IsPublic))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0002,
                eventArgument.GetLocation(),
                methodName));
        }

        if (isNetworkEvent)
        {
            ValidateNetworkNaming(context, eventArgument, methodName);

            var argumentCount = invocation.ArgumentList.Arguments.Count;
            if (argumentCount > 2 && !candidates.Any(candidate => candidate.HasNetworkCallable))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0004,
                    eventArgument.GetLocation(),
                    methodName));
            }

            if (argumentCount > 2)
            {
                var payloadCount = argumentCount - 2;
                var matching = candidates.Where(candidate => candidate.ParameterTypes.Length == payloadCount).ToImmutableArray();
                if (matching.IsDefaultOrEmpty)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UshRuleDescriptors.Ush0005,
                        invocation.GetLocation(),
                        0,
                        methodName));
                }
                else
                {
                    var anyCompatible = matching.Any(candidate => PayloadMatchesTypesSyntaxOnly(invocation, candidate.ParameterTypes));
                    if (!anyCompatible)
                    {
                        for (var index = 0; index < payloadCount; index++)
                        {
                            var argumentExpression = invocation.ArgumentList.Arguments[index + 2].Expression;
                            var argumentType = ClassifyArgumentTypeName(argumentExpression);
                            if (argumentType is null)
                            {
                                break;
                            }

                            var expected = matching[0].ParameterTypes[index];
                            if (!TypeNamesCompatibleNormalized(expected, argumentType))
                            {
                                context.ReportDiagnostic(Diagnostic.Create(
                                    UshRuleDescriptors.Ush0005,
                                    argumentExpression.GetLocation(),
                                    index + 1,
                                    methodName));
                                break;
                            }
                        }
                    }
                }
            }

            var syncMode = UshAnalyzerUtilities.GetBehaviourSyncModeNameFromSyntax(targetType);
            if (string.Equals(syncMode, "None", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0006,
                    invocation.GetLocation(),
                    targetType.Identifier.Text));
            }
        }

        return true;
    }

    private static IEnumerable<SyntaxMethodCandidate> FindCandidateMethodsBySyntax(TypeDeclarationSyntax typeDeclaration, string methodName)
    {
        foreach (var method in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!string.Equals(method.Identifier.Text, methodName, StringComparison.Ordinal))
            {
                continue;
            }

            var isPublic = method.Modifiers.Any(SyntaxKind.PublicKeyword);
            var hasNetworkCallable = UshAnalyzerUtilities.HasAttributeSyntax(method.AttributeLists, "NetworkCallable");
            var parameterTypes = (method.ParameterList?.Parameters ?? default)
                .Select(parameter => parameter.Type?.ToString().Trim() ?? string.Empty)
                .ToImmutableArray();

            yield return new SyntaxMethodCandidate(methodName, isPublic, hasNetworkCallable, parameterTypes);
        }
    }

    private static bool PayloadMatchesTypesSyntaxOnly(InvocationExpressionSyntax invocation, ImmutableArray<string> parameterTypes)
    {
        for (var index = 0; index < parameterTypes.Length; index++)
        {
            var argumentExpression = invocation.ArgumentList.Arguments[index + 2].Expression;
            var argumentType = ClassifyArgumentTypeName(argumentExpression);
            if (argumentType is null)
            {
                return true;
            }

            if (!TypeNamesCompatibleNormalized(parameterTypes[index], argumentType))
            {
                return false;
            }
        }

        return true;
    }

    private static string ExtractSimpleTypeName(string qualifiedName)
    {
        var text = qualifiedName.Trim();
        if (text.StartsWith("global::", StringComparison.Ordinal))
        {
            text = text.Substring("global::".Length);
        }

        var separatorIndex = text.LastIndexOf('.');
        return separatorIndex >= 0 ? text.Substring(separatorIndex + 1) : text;
    }

    private static string? ClassifyArgumentTypeName(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return "string";
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.CharacterLiteralExpression):
                return "char";
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression):
                return "null";
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                return ClassifyNumericLiteral(literal.Token.Text);
            case CastExpressionSyntax cast when cast.Type is { } typeSyntax:
                return typeSyntax.ToString().Trim();
            case ObjectCreationExpressionSyntax creation:
                return creation.Type.ToString().Trim();
        }

        return null;
    }

    private static string ClassifyNumericLiteral(string token)
    {
        if (token.EndsWith("f", StringComparison.OrdinalIgnoreCase))
        {
            return "float";
        }

        if (token.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            return "double";
        }

        if (token.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            return "decimal";
        }

        if (token.EndsWith("ul", StringComparison.OrdinalIgnoreCase) || token.EndsWith("lu", StringComparison.OrdinalIgnoreCase))
        {
            return "ulong";
        }

        if (token.EndsWith("u", StringComparison.OrdinalIgnoreCase))
        {
            return "uint";
        }

        if (token.EndsWith("l", StringComparison.OrdinalIgnoreCase))
        {
            return "long";
        }

        return "int";
    }

    private static bool TypeNamesCompatibleNormalized(string parameterType, string argumentType)
    {
        static string Normalize(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith("global::", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring("global::".Length);
            }

            return trimmed switch
            {
                "System.Int32" => "int",
                "System.Int64" => "long",
                "System.UInt32" => "uint",
                "System.UInt64" => "ulong",
                "System.Single" => "float",
                "System.Double" => "double",
                "System.Decimal" => "decimal",
                "System.String" => "string",
                _ => trimmed
            };
        }

        var normalizedParameter = Normalize(parameterType);
        var normalizedArgument = Normalize(argumentType);

        if (normalizedArgument == "null")
        {
            return true;
        }

        if (IsNumericAlias(normalizedParameter) && IsNumericAlias(normalizedArgument))
        {
            return true;
        }

        return string.Equals(normalizedParameter, normalizedArgument, StringComparison.Ordinal);
    }

    private static bool IsNumericAlias(string value)
    {
        return value is "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double" or "decimal";
    }

    private static string? GetLeftMostIdentifier(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.Text;
                case MemberAccessExpressionSyntax memberAccess:
                    expression = memberAccess.Expression;
                    continue;
                case ConditionalAccessExpressionSyntax conditional:
                    expression = conditional.WhenNotNull;
                    continue;
                default:
                    return null;
            }
        }
    }

    private static string? ExtractMethodNameFromTarget(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        if (UshAnalyzerUtilities.TryResolveConstantString(context.SemanticModel, expression, context.CancellationToken, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }

    private static string? TryExtractDeclaringTypeFromNameof(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.Text, "nameof", StringComparison.Ordinal) &&
            invocation.ArgumentList.Arguments.Count == 1)
        {
            var argumentExpression = invocation.ArgumentList.Arguments[0].Expression;
            if (argumentExpression is MemberAccessExpressionSyntax memberAccess)
            {
                var left = memberAccess.Expression;
                return ExtractSimpleTypeName(left.ToString());
            }
        }

        return null;
    }

    private static void ValidateTargetExistence(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax eventArgument,
        string methodName,
        ImmutableArray<IMethodSymbol> candidateMethods)
    {
        if (candidateMethods.IsDefaultOrEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0001,
                eventArgument.GetLocation(),
                methodName));
        }
    }

    private static void ValidateTargetAccessibility(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax eventArgument,
        string methodName,
        ImmutableArray<IMethodSymbol> candidateMethods)
    {
        foreach (var method in candidateMethods)
        {
            if (method.DeclaredAccessibility != Accessibility.Public)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0002,
                    eventArgument.GetLocation(),
                    methodName));
                return;
            }
        }
    }

    private static void ValidateNetworkNaming(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax eventArgument,
        string methodName)
    {
        if (methodName.StartsWith("_", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0003,
                eventArgument.GetLocation(),
                methodName));
        }
    }

    private static void ValidateNetworkCallableAttribute(
        SyntaxNodeAnalysisContext context,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ArgumentSyntax eventArgument,
        ImmutableArray<IMethodSymbol> candidateMethods,
        string methodName,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= 2)
        {
            return;
        }

        if (candidateMethods.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var method in candidateMethods)
        {
            if (UshAnalyzerUtilities.HasNetworkCallableAttribute(method, cancellationToken))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            UshRuleDescriptors.Ush0004,
            eventArgument.GetLocation(),
            methodName));
    }

    private static void ValidateNetworkParameterTypes(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ImmutableArray<IMethodSymbol> candidateMethods,
        string methodName)
    {
        if (arguments.Count <= 2)
        {
            return;
        }

        var payloadCount = arguments.Count - 2;
        var candidate = candidateMethods.FirstOrDefault(method => method.Parameters.Length == payloadCount);

        if (candidate is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0005,
                invocation.GetLocation(),
                0,
                methodName));
            return;
        }

        for (var index = 0; index < payloadCount; index++)
        {
            var argumentExpression = arguments[index + 2].Expression;
            var parameterType = candidate.Parameters[index].Type;
            var conversion = context.SemanticModel.ClassifyConversion(argumentExpression, parameterType);

            if (!conversion.IsIdentity && !conversion.IsImplicit)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0005,
                    argumentExpression.GetLocation(),
                    index + 1,
                    methodName));
                return;
            }
        }
    }

    private static void ValidateNetworkSyncMode(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol targetBehaviour,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (UshAnalyzerUtilities.IsBehaviourSyncMode(targetBehaviour, "None", semanticModel, cancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0006,
                invocation.GetLocation(),
                targetBehaviour.Name));
        }
    }

    private static bool TryIdentifyEventInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol? methodSymbol,
        out string methodName,
        out bool isCustomEvent,
        out bool isNetworkEvent)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (methodSymbol is not null)
        {
            methodName = methodSymbol.Name;
        }
        else
        {
            methodName = GetMethodNameFromExpression(invocation.Expression) ?? string.Empty;
        }

        isCustomEvent = CustomEventMethodNames.Contains(methodName);
        isNetworkEvent = NetworkEventMethodNames.Contains(methodName);
        return isCustomEvent || isNetworkEvent;
    }

    private static string? GetMethodNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => GetSimpleName(memberAccess.Name),
            MemberBindingExpressionSyntax memberBinding => GetSimpleName(memberBinding.Name),
            ConditionalAccessExpressionSyntax conditional => GetMethodNameFromExpression(conditional.WhenNotNull),
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => null,
        };
    }

    private static string? GetSimpleName(SimpleNameSyntax nameSyntax)
    {
        return nameSyntax.Identifier.Text;
    }

    private static INamedTypeSymbol? ResolveBehaviourFromMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionType = UshAnalyzerUtilities.GetExpressionType(semanticModel, memberAccess.Expression, cancellationToken);
        if (expressionType is INamedTypeSymbol named && UshAnalyzerUtilities.IsUdonSharpBehaviour(named))
        {
            return named;
        }

        var symbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        switch (symbol)
        {
            case ILocalSymbol local when UshAnalyzerUtilities.IsUdonSharpBehaviour(local.Type as INamedTypeSymbol):
                return (INamedTypeSymbol)local.Type;
            case IFieldSymbol field when UshAnalyzerUtilities.IsUdonSharpBehaviour(field.Type as INamedTypeSymbol):
                return (INamedTypeSymbol)field.Type;
            case IPropertySymbol property when UshAnalyzerUtilities.IsUdonSharpBehaviour(property.Type as INamedTypeSymbol):
                return (INamedTypeSymbol)property.Type;
        }

        return expressionType as INamedTypeSymbol;
    }

    private static INamedTypeSymbol? DetermineTargetBehaviour(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol,
        IMethodSymbol? resolvedMethodSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (resolvedMethodSymbol?.ContainingType is INamedTypeSymbol containing &&
            UshAnalyzerUtilities.IsUdonSharpBehaviour(containing))
        {
            return containing;
        }

        if (methodSymbol is null)
        {
            return null;
        }

        return ResolveTargetBehaviour(invocation, methodSymbol, semanticModel, cancellationToken);
    }

    private static INamedTypeSymbol? ResolveTargetBehaviour(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var type = ResolveBehaviourFromMemberAccess(memberAccess, semanticModel, cancellationToken);
            if (type is not null)
            {
                return type;
            }
        }

        var containingType = UshAnalyzerUtilities.GetContainingType(invocation, semanticModel, cancellationToken);
        if (containingType is not null)
        {
            return containingType;
        }

        return symbol.ContainingType as INamedTypeSymbol;
    }

    private sealed class MethodSymbolComparer : IEqualityComparer<IMethodSymbol>
    {
        public static readonly MethodSymbolComparer Instance = new();

        public bool Equals(IMethodSymbol? x, IMethodSymbol? y)
        {
            return SymbolEqualityComparer.Default.Equals(x, y);
        }

        public int GetHashCode(IMethodSymbol obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj);
        }
    }
}
