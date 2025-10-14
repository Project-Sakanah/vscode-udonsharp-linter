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
public sealed class UsnForbiddenNamespaceAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<string> ForbiddenNamespacePrefixes = ImmutableArray.Create(
        "System.IO",
        "System.Net",
        "System.Reflection",
        "System.Threading",
        "System.Diagnostics",
        "System.Security",
        "System.Runtime.InteropServices",
        "System.Linq",
        "System.Web"
    );

    private static readonly ImmutableArray<string> ForbiddenTypeNames = ImmutableArray.Create(
        "System.Net.WebClient",
        "System.Net.Http.HttpClient",
        "System.Threading.Tasks.Task",
        "System.Diagnostics.Process",
        "System.Random"
    );

    private static readonly DiagnosticDescriptor ForbiddenNamespaceRule = new(
        "USL020",
        "Forbidden .NET API",
        "'{0}' is blocked in UdonSharp for security or platform reasons.",
        "UdonSharp.API",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL020");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ForbiddenNamespaceRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var directive = (UsingDirectiveSyntax)context.Node;
        var namespaceName = directive.Name.ToString();
        if (IsForbiddenNamespace(namespaceName))
        {
            Report(context, directive.Name.GetLocation(), namespaceName);
        }
    }

    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
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

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
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

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context.Node, context))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(((ObjectCreationExpressionSyntax)context.Node).Type, context.CancellationToken).Symbol as INamedTypeSymbol;
        if (symbol is null)
        {
            return;
        }

        CheckSymbol(context, ((ObjectCreationExpressionSyntax)context.Node).Type.GetLocation(), symbol);
    }

    private static void CheckSymbol(SyntaxNodeAnalysisContext context, Location location, ISymbol symbol)
    {
        var typeSymbol = symbol switch
        {
            IMethodSymbol method => method.ContainingType,
            IPropertySymbol property => property.ContainingType,
            IFieldSymbol field => field.ContainingType,
            IEventSymbol eventSymbol => eventSymbol.ContainingType,
            INamedTypeSymbol namedType => namedType,
            _ => symbol.ContainingType ?? symbol as INamedTypeSymbol
        };

        if (typeSymbol is null)
        {
            return;
        }

        var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (ForbiddenTypeNames.Contains(fullName))
        {
            Report(context, location, fullName);
            return;
        }

        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (IsForbiddenNamespace(namespaceName))
        {
            Report(context, location, namespaceName);
        }
    }

    private static bool IsForbiddenNamespace(string namespaceName)
    {
        return ForbiddenNamespacePrefixes.Any(prefix => namespaceName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static void Report(SyntaxNodeAnalysisContext context, Location location, string name)
    {
        context.ReportDiagnostic(Diagnostic.Create(ForbiddenNamespaceRule, location, name));
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
        if (symbol is null)
        {
            return false;
        }

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
