using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshRuntimeRestrictionAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0016,
        UshRuleDescriptors.Ush0017,
        UshRuleDescriptors.Ush0018,
        UshRuleDescriptors.Ush0019,
        UshRuleDescriptors.Ush0020,
        UshRuleDescriptors.Ush0021);

    private static readonly Dictionary<string, ImmutableArray<string>> EventSignatures = new(StringComparer.Ordinal)
    {
        ["OnStationEntered"] = ImmutableArray.Create("VRC.SDKBase.VRCPlayerApi"),
        ["OnStationExited"] = ImmutableArray.Create("VRC.SDKBase.VRCPlayerApi"),
        ["OnOwnershipTransferred"] = ImmutableArray.Create("VRC.SDKBase.VRCPlayerApi"),
        ["OnPlayerJoined"] = ImmutableArray.Create("VRC.SDKBase.VRCPlayerApi"),
        ["OnPlayerLeft"] = ImmutableArray.Create("VRC.SDKBase.VRCPlayerApi")
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeIsExpression, SyntaxKind.IsExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAsExpression, SyntaxKind.AsExpression);
        context.RegisterSyntaxNodeAction(AnalyzeTryStatement, SyntaxKind.TryStatement);
        context.RegisterSyntaxNodeAction(AnalyzeThrowStatement, SyntaxKind.ThrowStatement);
        context.RegisterSyntaxNodeAction(AnalyzeThrowExpression, SyntaxKind.ThrowExpression);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (methodSymbol is null)
        {
            return;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType is null || !UshAnalyzerUtilities.IsUdonSharpBehaviour(containingType))
        {
            return;
        }

        if (!EventSignatures.TryGetValue(methodSymbol.Name, out var expectedParameters))
        {
            return;
        }

        if (methodSymbol.Parameters.Length != expectedParameters.Length)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0016,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name));
            return;
        }

        for (var index = 0; index < expectedParameters.Length; index++)
        {
            var parameterType = methodSymbol.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (!string.Equals(parameterType, expectedParameters[index], StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0016,
                    methodDeclaration.ParameterList.Parameters[index].GetLocation(),
                    methodSymbol.Name));
                return;
            }
        }

        if (methodSymbol.DeclaredAccessibility != Accessibility.Public || !methodSymbol.IsOverride)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0016,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        var methodName = methodSymbol?.Name ?? GetInvocationMethodName(invocation.Expression);
        if (!string.Equals(methodName, "Instantiate", StringComparison.Ordinal))
        {
            return;
        }

        var isUnityInstantiate = IsUnityObjectInvocation(methodSymbol, invocation);
        if (!isUnityInstantiate)
        {
            if (methodSymbol is null &&
                invocation.Expression is IdentifierNameSyntax identifier &&
                string.Equals(identifier.Identifier.Text, "Instantiate", StringComparison.Ordinal))
            {
                var containingType = UshAnalyzerUtilities.GetContainingType(invocation, context.SemanticModel, context.CancellationToken);
                if (containingType is INamedTypeSymbol namedType && UshAnalyzerUtilities.IsUdonSharpBehaviour(namedType))
                {
                    isUnityInstantiate = true;
                }
            }

            if (!isUnityInstantiate)
            {
                return;
            }
        }

        if (methodSymbol?.IsGenericMethod == true && methodSymbol.TypeArguments.Length > 0)
        {
            var typeArgument = methodSymbol.TypeArguments[0];
            if (!IsGameObject(typeArgument))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0017,
                    invocation.GetLocation()));
            }

            return;
        }

        if (methodSymbol is null &&
            TryGetGenericTypeArgument(invocation.Expression, out var genericTypeName))
        {
            if (!IsGameObjectTypeName(genericTypeName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0017,
                    invocation.GetLocation()));
            }

            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        var argumentExpression = invocation.ArgumentList.Arguments[0].Expression;
        if (methodSymbol is null)
        {
            if (argumentExpression is ObjectCreationExpressionSyntax objectCreation &&
                !IsGameObjectTypeName(objectCreation.Type.ToString()))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0017,
                    invocation.GetLocation()));
            }

            return;
        }

        var argumentType = UshAnalyzerUtilities.GetExpressionType(context.SemanticModel, argumentExpression, context.CancellationToken);
        if (argumentType is null)
        {
            return;
        }

        if (!IsGameObject(argumentType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0017,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeIsExpression(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UshRuleDescriptors.Ush0018,
            context.Node.GetLocation()));
    }

    private static void AnalyzeAsExpression(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UshRuleDescriptors.Ush0019,
            context.Node.GetLocation()));
    }

    private static void AnalyzeTryStatement(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UshRuleDescriptors.Ush0020,
            context.Node.GetLocation()));
    }

    private static void AnalyzeThrowStatement(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UshRuleDescriptors.Ush0021,
            context.Node.GetLocation()));
    }

    private static void AnalyzeThrowExpression(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UshRuleDescriptors.Ush0021,
            context.Node.GetLocation()));
    }

    private static bool IsUnityObjectInvocation(IMethodSymbol? methodSymbol, InvocationExpressionSyntax invocation)
    {
        if (methodSymbol?.OriginalDefinition?.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is { } containing &&
            string.Equals(containing, "global::UnityEngine.Object", StringComparison.Ordinal))
        {
            return true;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var qualifier = memberAccess.Expression.ToString().Trim();
            if (string.Equals(qualifier, "UnityEngine.Object", StringComparison.Ordinal) ||
                string.Equals(qualifier, "global::UnityEngine.Object", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetGenericTypeArgument(ExpressionSyntax expression, out string typeName)
    {
        switch (expression)
        {
            case GenericNameSyntax generic when generic.TypeArgumentList.Arguments.Count > 0:
                typeName = generic.TypeArgumentList.Arguments[0].ToString();
                return true;
            case MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } when generic.TypeArgumentList.Arguments.Count > 0:
                typeName = generic.TypeArgumentList.Arguments[0].ToString();
                return true;
            case ConditionalAccessExpressionSyntax conditional:
                return TryGetGenericTypeArgument(conditional.WhenNotNull, out typeName);
            default:
                typeName = string.Empty;
                return false;
        }
    }

    private static string? GetInvocationMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            ConditionalAccessExpressionSyntax conditional => GetInvocationMethodName(conditional.WhenNotNull),
            _ => null,
        };
    }

    private static bool IsGameObjectTypeName(string typeName)
    {
        var trimmed = typeName.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("global::", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring("global::".Length);
        }

        return string.Equals(trimmed, "UnityEngine.GameObject", StringComparison.Ordinal) ||
               string.Equals(trimmed, "GameObject", StringComparison.Ordinal);
    }

    private static bool IsGameObject(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (typeSymbol is IArrayTypeSymbol array)
        {
            return IsGameObject(array.ElementType);
        }

        return string.Equals(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            "UnityEngine.GameObject",
            StringComparison.Ordinal);
    }
}
