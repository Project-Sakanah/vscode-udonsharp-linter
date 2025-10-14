using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null)
        {
            return;
        }

        if (!string.Equals(symbol.Name, "Instantiate", StringComparison.Ordinal) ||
            !string.Equals(symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), "UnityEngine.Object", StringComparison.Ordinal))
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        var argumentType = context.SemanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, context.CancellationToken).Type;
        if (argumentType is null)
        {
            return;
        }

        var typeName = argumentType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!string.Equals(typeName, "UnityEngine.GameObject", StringComparison.Ordinal))
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
}
