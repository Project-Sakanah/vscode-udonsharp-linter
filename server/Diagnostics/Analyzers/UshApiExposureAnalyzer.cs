using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshApiExposureAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0013,
        UshRuleDescriptors.Ush0014,
        UshRuleDescriptors.Ush0015);

    private static readonly HashSet<string> ForbiddenNamespaces = new(StringComparer.Ordinal)
    {
        "System.IO",
        "System.Net",
        "System.Reflection",
        "System.Threading",
        "System.Diagnostics",
        "System.Security",
        "System.Runtime.InteropServices",
        "System.Web",
        "UnityEditor"
    };

    private static readonly HashSet<string> ForbiddenTypes = new(StringComparer.Ordinal)
    {
        "System.IntPtr",
        "System.UIntPtr",
        "System.Diagnostics.Process",
        "System.Random",
        "System.Net.Http.HttpClient",
        "System.Net.WebClient",
        "System.Threading.Tasks.Task"
    };

    private static readonly HashSet<string> ForbiddenMethods = new(StringComparer.Ordinal)
    {
        "UnityEngine.Component.GetComponent",
        "UnityEngine.Component.GetComponents",
        "UnityEngine.GameObject.GetComponent",
        "UnityEngine.GameObject.GetComponents"
    };

    private static readonly HashSet<string> ForbiddenMembers = new(StringComparer.Ordinal)
    {
        "UnityEngine.Component.gameObject",
        "UnityEngine.GameObject.gameObject"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeTypeUsage, SyntaxKind.IdentifierName);
        context.RegisterSyntaxNodeAction(AnalyzeTypeUsage, SyntaxKind.QualifiedName);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null)
        {
            return;
        }

        if (ForbiddenMethods.Contains(GetMemberDisplayName(symbol)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0013,
                invocation.GetLocation(),
                symbol.Name));
            return;
        }

        var namespaceName = symbol.ContainingNamespace?.ToDisplayString();
        if (namespaceName is not null && IsForbiddenNamespace(namespaceName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0013,
                invocation.GetLocation(),
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
        if (symbol is null)
        {
            return;
        }

        if (symbol is IFieldSymbol or IPropertySymbol)
        {
            var name = GetMemberDisplayName(symbol);
            if (ForbiddenMembers.Contains(name))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0014,
                    access.Name.GetLocation(),
                    symbol.Name));
                return;
            }

            var namespaceName = symbol.ContainingType?.ContainingNamespace?.ToDisplayString();
            if (namespaceName is not null && IsForbiddenNamespace(namespaceName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0014,
                    access.Name.GetLocation(),
                    symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }
        }
    }

    private static void AnalyzeTypeUsage(SyntaxNodeAnalysisContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is null)
        {
            return;
        }

        if (symbol is ITypeSymbol type)
        {
            var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (ForbiddenTypes.Contains(typeName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0015,
                    context.Node.GetLocation(),
                    typeName));
                return;
            }

            if (IsForbiddenNamespace(type.ContainingNamespace?.ToDisplayString()))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0015,
                    context.Node.GetLocation(),
                    typeName));
            }
        }
    }

    private static bool IsForbiddenNamespace(string? namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
        {
            return false;
        }

        foreach (var prefix in ForbiddenNamespaces)
        {
            if (namespaceName!.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetMemberDisplayName(ISymbol symbol)
    {
        return $"{symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}.{symbol.Name}";
    }
}
