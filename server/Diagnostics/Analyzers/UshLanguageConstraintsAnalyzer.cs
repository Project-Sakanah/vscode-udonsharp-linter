using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshLanguageConstraintsAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0022,
        UshRuleDescriptors.Ush0023,
        UshRuleDescriptors.Ush0024,
        UshRuleDescriptors.Ush0025,
        UshRuleDescriptors.Ush0026,
        UshRuleDescriptors.Ush0027,
        UshRuleDescriptors.Ush0028,
        UshRuleDescriptors.Ush0029,
        UshRuleDescriptors.Ush0030,
        UshRuleDescriptors.Ush0031,
        UshRuleDescriptors.Ush0032,
        UshRuleDescriptors.Ush0033,
        UshRuleDescriptors.Ush0034,
        UshRuleDescriptors.Ush0035,
        UshRuleDescriptors.Ush0036,
        UshRuleDescriptors.Ush0037,
        UshRuleDescriptors.Ush0038,
        UshRuleDescriptors.Ush0039);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeNullableType, SyntaxKind.NullableType);
        context.RegisterSyntaxNodeAction(AnalyzeConditionalAccess, SyntaxKind.ConditionalAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeArrayType, SyntaxKind.ArrayType);
        context.RegisterSyntaxNodeAction(AnalyzeImplicitArrayCreation, SyntaxKind.ImplicitArrayCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeElementAccess, SyntaxKind.ElementAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.StructDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeInitializer, SyntaxKind.ObjectInitializerExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInitializer, SyntaxKind.CollectionInitializerExpression);
        context.RegisterSyntaxNodeAction(AnalyzeTypeOf, SyntaxKind.TypeOfExpression);
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeGotoStatement, SyntaxKind.GotoStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLabeledStatement, SyntaxKind.LabeledStatement);
        context.RegisterSyntaxNodeAction(AnalyzeGotoCase, SyntaxKind.GotoCaseStatement);
        context.RegisterSyntaxNodeAction(AnalyzeGotoDefault, SyntaxKind.GotoDefaultStatement);
    }

    private static void AnalyzeNullableType(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0022,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeConditionalAccess(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0023,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeArrayType(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context, context.Node))
        {
            return;
        }

        var arrayType = (ArrayTypeSyntax)context.Node;
        foreach (var rankSpecifier in arrayType.RankSpecifiers)
        {
            if (rankSpecifier.Rank > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0024,
                    rankSpecifier.GetLocation()));
            }
        }
    }

    private static void AnalyzeImplicitArrayCreation(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context, context.Node))
        {
            return;
        }

        var implicitArray = (ImplicitArrayCreationExpressionSyntax)context.Node;
        if (implicitArray.Initializer is InitializerExpressionSyntax initializer &&
            initializer.Expressions.OfType<InitializerExpressionSyntax>().Any(nested => nested.Expressions.Count > 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0024,
                implicitArray.GetLocation()));
        }
    }

    private static void AnalyzeElementAccess(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context, context.Node))
        {
            return;
        }

        var elementAccess = (ElementAccessExpressionSyntax)context.Node;
        if (elementAccess.ArgumentList.Arguments.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0025,
                elementAccess.ArgumentList.GetLocation()));
        }
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0026,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        if (!IsWithinUdonSharpScript(context, typeDeclaration))
        {
            return;
        }

        if (typeDeclaration.Parent is TypeDeclarationSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0027,
                typeDeclaration.Identifier.GetLocation()));
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken);
        if (symbol is INamedTypeSymbol named && UshAnalyzerUtilities.IsUdonSharpBehaviour(named))
        {
            if (typeDeclaration.BaseList?.Types.Any(type =>
                    context.SemanticModel.GetTypeInfo(type.Type, context.CancellationToken).Type is INamedTypeSymbol baseType &&
                    baseType.TypeKind == TypeKind.Interface) == true)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0030,
                    typeDeclaration.Identifier.GetLocation()));
            }
        }
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0028,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
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

        if (methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0035,
                methodDeclaration.Identifier.GetLocation()));
        }

        if (methodDeclaration.TypeParameterList is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0029,
                methodDeclaration.Identifier.GetLocation()));
        }

        if (!methodSymbol.IsOverride)
        {
            foreach (var baseMethod in containingType.BaseType?.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>() ?? Enumerable.Empty<IMethodSymbol>())
            {
                if (HasMatchingSignature(baseMethod, methodSymbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UshRuleDescriptors.Ush0031,
                        methodDeclaration.Identifier.GetLocation()));
                    break;
                }
            }
        }
    }

    private static bool HasMatchingSignature(IMethodSymbol baseMethod, IMethodSymbol method)
    {
        if (baseMethod.Parameters.Length != method.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < baseMethod.Parameters.Length; index++)
        {
            var baseParameter = baseMethod.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var currentParameter = method.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (!string.Equals(baseParameter, currentParameter, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AnalyzeInitializer(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0032,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeTypeOf(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context, context.Node))
        {
            return;
        }

        var typeOf = (TypeOfExpressionSyntax)context.Node;
        var typeSymbol = context.SemanticModel.GetTypeInfo(typeOf.Type, context.CancellationToken).Type as INamedTypeSymbol;
        if (typeSymbol is not null && UshAnalyzerUtilities.IsUdonSharpBehaviour(typeSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0033,
                typeOf.Type.GetLocation()));
        }
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context, context.Node))
        {
            return;
        }

        var fieldDeclaration = (FieldDeclarationSyntax)context.Node;
        if (fieldDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0034,
                fieldDeclaration.Modifiers.First(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)).GetLocation()));
        }
    }

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (!IsWithinUdonSharpScript(context, context.Node))
        {
            return;
        }

        var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
        if (propertyDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0034,
                propertyDeclaration.Modifiers.First(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)).GetLocation()));
        }
    }

    private static void AnalyzeGotoStatement(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0036,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeLabeledStatement(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0037,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeGotoCase(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0038,
                context.Node.GetLocation()));
        }
    }

    private static void AnalyzeGotoDefault(SyntaxNodeAnalysisContext context)
    {
        if (IsWithinUdonSharpScript(context, context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0039,
                context.Node.GetLocation()));
        }
    }

    private static bool IsWithinUdonSharpScript(SyntaxNodeAnalysisContext context, SyntaxNode node)
    {
        var type = UshAnalyzerUtilities.GetContainingType(node, context.SemanticModel, context.CancellationToken);
        return type is not null && UshAnalyzerUtilities.IsUdonSharpBehaviour(type);
    }
}
