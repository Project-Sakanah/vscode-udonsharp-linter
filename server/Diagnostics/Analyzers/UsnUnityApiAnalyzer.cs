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
public sealed class UsnUnityApiAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor ForbiddenUnityApiRule = new(
        "USL021",
        "Forbidden Unity API",
        "The Unity API '{0}' is not available in VRChat Udon.",
        "UdonSharp.Unity",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL021");

    private static readonly DiagnosticDescriptor RestrictedFeatureRule = new(
        "USL022",
        "Restricted local-only feature",
        "'{0}' is restricted in VRChat. Consider using the VRC-provided alternative.",
        "UdonSharp.Unity",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL022");

    private static readonly ImmutableArray<string> ForbiddenNamespaces = ImmutableArray.Create(
        "UnityEditor"
    );

    private static readonly ImmutableArray<string> RestrictedTypes = ImmutableArray.Create(
        "UnityEngine.Camera",
        "UnityEngine.Microphone",
        "UnityEngine.Application",
        "UnityEngine.Input"
    );

    private static readonly ImmutableArray<string> RestrictedMembers = ImmutableArray.Create(
        "UnityEngine.Input.GetAxis",
        "UnityEngine.Input.GetKey",
        "UnityEngine.Input.GetKeyDown",
        "UnityEngine.Input.GetButton",
        "UnityEngine.Input.GetMouseButton",
        "UnityEngine.Application.Quit",
        "UnityEngine.Camera.main"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ForbiddenUnityApiRule, RestrictedFeatureRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeMember, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var directive = (UsingDirectiveSyntax)context.Node;
        var namespaceName = directive.Name.ToString();
        if (ForbiddenNamespaces.Any(ns => namespaceName.StartsWith(ns, StringComparison.Ordinal)))
        {
            context.ReportDiagnostic(Diagnostic.Create(ForbiddenUnityApiRule, directive.Name.GetLocation(), namespaceName));
        }
    }

    private static void AnalyzeMember(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context.Node, context))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is null)
        {
            return;
        }

        CheckSymbol(context, context.Node.GetLocation(), symbol);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context.Node, context))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is null)
        {
            return;
        }

        CheckSymbol(context, ((InvocationExpressionSyntax)context.Node).Expression.GetLocation(), symbol);
    }

    private static void CheckSymbol(SyntaxNodeAnalysisContext context, Location location, ISymbol symbol)
    {
        var typeSymbol = symbol switch
        {
            IMethodSymbol method => method.ContainingType,
            IPropertySymbol property => property.ContainingType,
            IFieldSymbol field => field.ContainingType,
            _ => symbol as INamedTypeSymbol ?? symbol.ContainingType
        };

        if (typeSymbol is null)
        {
            return;
        }

        var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (ForbiddenNamespaces.Any(ns => fullName.StartsWith(ns, StringComparison.Ordinal)))
        {
            context.ReportDiagnostic(Diagnostic.Create(ForbiddenUnityApiRule, location, fullName));
            return;
        }

        if (RestrictedTypes.Contains(fullName))
        {
            var memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (RestrictedMembers.Any(restricted => memberName.StartsWith(restricted, StringComparison.Ordinal)))
            {
                context.ReportDiagnostic(Diagnostic.Create(RestrictedFeatureRule, location, memberName));
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(ForbiddenUnityApiRule, location, memberName));
            }
        }
        else
        {
            var memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (RestrictedMembers.Any(restricted => memberName.StartsWith(restricted, StringComparison.Ordinal)))
            {
                context.ReportDiagnostic(Diagnostic.Create(RestrictedFeatureRule, location, memberName));
            }
        }
    }

    private static bool IsWithinUdonSharpScript(SyntaxNode node, SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration is null)
        {
            return false;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken) as INamedTypeSymbol;
        return symbol is not null && IsPotentialUdonScript(symbol);
    }

    private static bool IsPotentialUdonScript(INamedTypeSymbol symbol)
    {
        if (InheritsUdonSharpBehaviour(symbol))
        {
            return true;
        }

        if (symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name?.Contains("Udon", StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        foreach (var member in symbol.GetMembers())
        {
            if (member.GetAttributes().Any(attribute => attribute.AttributeClass?.Name?.Contains("Udon", StringComparison.OrdinalIgnoreCase) == true))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsUdonSharpBehaviour(INamedTypeSymbol? symbol)
    {
        while (symbol is not null)
        {
            if (string.Equals(symbol.Name, "UdonSharpBehaviour", StringComparison.Ordinal))
            {
                return true;
            }

            symbol = symbol.BaseType;
        }

        return false;
    }
}
