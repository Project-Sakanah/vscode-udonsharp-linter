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
public sealed class UsnLanguageConstraintsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor GenericClassRule = new(
        id: "USL001",
        title: "Generic classes or instance methods are not supported",
        messageFormat: "UdonSharp does not support generic {0}.",
        category: "UdonSharp.Language",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL001");

    private static readonly DiagnosticDescriptor InterfaceRule = new(
        id: "USL002",
        title: "Interfaces are not supported",
        messageFormat: "Interfaces cannot be declared or implemented in UdonSharp scripts.",
        category: "UdonSharp.Language",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL002");

    private static readonly DiagnosticDescriptor BaseClassRule = new(
        id: "USL003",
        title: "UdonSharpBehaviour inheritance required",
        messageFormat: "UdonSharp scripts must derive from UdonSharpBehaviour.",
        category: "UdonSharp.Language",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL003");

    private static readonly DiagnosticDescriptor MethodOverloadRule = new(
        id: "USL004",
        title: "Method overloading is not supported",
        messageFormat: "Method '{0}' is overloaded. UdonSharp does not support overloaded instance methods.",
        category: "UdonSharp.Language",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL004");

    private static readonly DiagnosticDescriptor DelegateRule = new(
        id: "USL005",
        title: "Delegates, events, and lambdas are not supported",
        messageFormat: "UdonSharp does not support delegates, events, or lambda expressions.",
        category: "UdonSharp.Language",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL005");

    private static readonly DiagnosticDescriptor UnsupportedSyntaxRule = new(
        id: "USL006",
        title: "Unsupported C# construct",
        messageFormat: "'{0}' is not supported in UdonSharp.",
        category: "UdonSharp.Language",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL006");

    private static readonly DiagnosticDescriptor RefOutRule = new(
        id: "USL007",
        title: "ref/out/in parameters are restricted",
        messageFormat: "ref/out/in parameters are restricted in UdonSharp and may not behave as expected.",
        category: "UdonSharp.Language",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL007");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GenericClassRule,
        InterfaceRule,
        BaseClassRule,
        MethodOverloadRule,
        DelegateRule,
        UnsupportedSyntaxRule,
        RefOutRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeClassSyntax, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethodSyntax, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeInterfaceSyntax, SyntaxKind.InterfaceDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeDelegateSyntax, SyntaxKind.DelegateDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeEventSyntax, SyntaxKind.EventDeclaration, SyntaxKind.EventFieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLambdaSyntax, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
        context.RegisterSyntaxNodeAction(AnalyzeUnsupportedSyntax, SyntaxKind.AwaitExpression, SyntaxKind.YieldReturnStatement, SyntaxKind.YieldBreakStatement, SyntaxKind.GotoStatement, SyntaxKind.LockStatement, SyntaxKind.UnsafeStatement);
        context.RegisterSyntaxNodeAction(AnalyzeDynamicIdentifier, SyntaxKind.IdentifierName);
        context.RegisterSyntaxNodeAction(AnalyzeParameters, SyntaxKind.Parameter);
        context.RegisterSyntaxNodeAction(AnalyzeArguments, SyntaxKind.Argument);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        if (!IsPotentialUdonScript(namedType))
        {
            return;
        }

        if (!InheritsUdonSharpBehaviour(namedType))
        {
            foreach (var syntax in namedType.DeclaringSyntaxReferences)
            {
                var node = syntax.GetSyntax(context.CancellationToken);
                context.ReportDiagnostic(Diagnostic.Create(BaseClassRule, node.GetLocation()));
            }
        }

        var overloadGroups = namedType.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary && !method.IsStatic && !method.IsOverride)
            .GroupBy(method => method.Name, StringComparer.Ordinal);

        foreach (var group in overloadGroups)
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            foreach (var method in group)
            {
                foreach (var syntax in method.DeclaringSyntaxReferences)
                {
                    context.ReportDiagnostic(Diagnostic.Create(MethodOverloadRule, syntax.GetSyntax(context.CancellationToken).GetLocation(), method.Name));
                }
            }
        }
    }

    private static void AnalyzeClassSyntax(SyntaxNodeAnalysisContext context)
    {
        var node = (ClassDeclarationSyntax)context.Node;
        if (node.TypeParameterList is null)
        {
            return;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken);
        if (symbol is null)
        {
            return;
        }

        if (node.TypeParameterList.Parameters.Count > 0 && InheritsUdonSharpBehaviour(symbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(GenericClassRule, node.TypeParameterList.GetLocation()));
        }

        if (node.BaseList is null)
        {
            return;
        }

        foreach (var baseType in node.BaseList.Types)
        {
            var type = context.SemanticModel.GetTypeInfo(baseType.Type, context.CancellationToken).Type;
            if (type is ITypeSymbol interfaceType && interfaceType.TypeKind == TypeKind.Interface && InheritsUdonSharpBehaviour(symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(InterfaceRule, baseType.GetLocation()));
            }
        }
    }

    private static void AnalyzeMethodSyntax(SyntaxNodeAnalysisContext context)
    {
        var node = (MethodDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken);
        if (symbol?.ContainingType is null)
        {
            return;
        }

        if (!IsPotentialUdonScript(symbol.ContainingType))
        {
            return;
        }

        if (!symbol.IsStatic && node.TypeParameterList is { Parameters.Count: > 0 })
        {
            context.ReportDiagnostic(Diagnostic.Create(GenericClassRule, node.TypeParameterList.GetLocation(), "instance methods"));
        }

        if (node.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            var asyncToken = node.Modifiers.First(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword));
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedSyntaxRule, asyncToken.GetLocation(), "async"));
        }

        if (node.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword)))
        {
            var unsafeToken = node.Modifiers.First(modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword));
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedSyntaxRule, unsafeToken.GetLocation(), "unsafe"));
        }
    }

    private static void AnalyzeInterfaceSyntax(SyntaxNodeAnalysisContext context)
    {
        var node = (InterfaceDeclarationSyntax)context.Node;
        context.ReportDiagnostic(Diagnostic.Create(InterfaceRule, node.Identifier.GetLocation()));
    }

    private static void AnalyzeDelegateSyntax(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context))
        {
            context.ReportDiagnostic(Diagnostic.Create(DelegateRule, context.Node.GetLocation()));
        }
    }

    private static void AnalyzeEventSyntax(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context))
        {
            context.ReportDiagnostic(Diagnostic.Create(DelegateRule, context.Node.GetLocation()));
        }
    }

    private static void AnalyzeLambdaSyntax(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context))
        {
            context.ReportDiagnostic(Diagnostic.Create(DelegateRule, context.Node.GetLocation()));
        }
    }

    private static void AnalyzeUnsupportedSyntax(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context))
        {
            return;
        }

        var tokenText = context.Node switch
        {
            AwaitExpressionSyntax => "await",
            YieldStatementSyntax y when y.ReturnOrBreakKeyword.IsKind(SyntaxKind.YieldKeyword) && y.YieldKeyword.IsKind(SyntaxKind.YieldKeyword) => "yield",
            GotoStatementSyntax => "goto",
            LockStatementSyntax => "lock",
            UnsafeStatementSyntax => "unsafe",
            _ => context.Node.ToString()
        };

        context.ReportDiagnostic(Diagnostic.Create(UnsupportedSyntaxRule, context.Node.GetLocation(), tokenText));
    }

    private static void AnalyzeDynamicIdentifier(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.RawKind != (int)SyntaxKind.IdentifierName)
        {
            return;
        }

        var identifier = (IdentifierNameSyntax)context.Node;
        if (!string.Equals(identifier.Identifier.Text, "dynamic", StringComparison.Ordinal))
        {
            return;
        }

        if (!IsWithinUdonSharpScript(context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(UnsupportedSyntaxRule, identifier.GetLocation(), "dynamic"));
    }

    private static void AnalyzeParameters(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (parameter.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.RefKeyword) || modifier.IsKind(SyntaxKind.OutKeyword) || modifier.IsKind(SyntaxKind.InKeyword)))
        {
            if (IsWithinUdonSharpScript(context))
            {
                context.ReportDiagnostic(Diagnostic.Create(RefOutRule, parameter.GetLocation()));
            }
        }
    }

    private static void AnalyzeArguments(SyntaxNodeAnalysisContext context)
    {
        var argument = (ArgumentSyntax)context.Node;
        if (argument.RefOrOutKeyword.IsKind(SyntaxKind.None))
        {
            return;
        }

        if (IsWithinUdonSharpScript(context))
        {
            context.ReportDiagnostic(Diagnostic.Create(RefOutRule, argument.GetLocation()));
        }
    }

    private static bool IsWithinUdonSharpScript(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = context.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration is null)
        {
            return false;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken) as INamedTypeSymbol;
        return symbol is not null && IsPotentialUdonScript(symbol);
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
}





