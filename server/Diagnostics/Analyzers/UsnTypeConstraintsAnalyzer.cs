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
public sealed class UsnTypeConstraintsAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> BlockedCollections = new(StringComparer.Ordinal)
    {
        "System.Collections.ArrayList",
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Queue`1",
        "System.Collections.Generic.Stack`1",
    };

    private static readonly HashSet<string> UnityDynamicCreationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityEngine.GameObject",
        "UnityEngine.Component",
        "UnityEngine.Transform",
        "UnityEngine.Rigidbody",
    };

    private static readonly DiagnosticDescriptor CollectionRule = new(
        "USL010",
        "Unsupported collection type",
        "The collection type '{0}' is not supported in UdonSharp.",
        "UdonSharp.Types",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL010");

    private static readonly DiagnosticDescriptor MultiDimensionalArrayRule = new(
        "USL011",
        "Multidimensional arrays are not supported",
        "Multidimensional arrays are not supported in UdonSharp. Use jagged arrays instead.",
        "UdonSharp.Types",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL011");

    private static readonly DiagnosticDescriptor UserTypeInstantiationRule = new(
        "USL012",
        "User-defined types cannot be instantiated",
        "UdonSharp cannot instantiate user-defined reference types at runtime.",
        "UdonSharp.Types",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL012");

    private static readonly DiagnosticDescriptor UnityDynamicCreationRule = new(
        "USL013",
        "Unity objects cannot be created dynamically",
        "'{0}' cannot be created or added dynamically in UdonSharp. Use prefabs or VRCInstantiate instead.",
        "UdonSharp.Types",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL013");

    private static readonly DiagnosticDescriptor UnsupportedValueTypeRule = new(
        "USL014",
        "Unsupported value type",
        "The value type '{0}' is not supported by UdonSharp.",
        "UdonSharp.Types",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL014");

    private static readonly DiagnosticDescriptor NumericCastRule = new(
        "USL015",
        "Potential numeric overflow",
        "Casting from '{0}' to '{1}' may overflow in UdonSharp. Validate the range before casting.",
        "UdonSharp.Types",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL015");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        CollectionRule,
        MultiDimensionalArrayRule,
        UserTypeInstantiationRule,
        UnityDynamicCreationRule,
        UnsupportedValueTypeRule,
        NumericCastRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeArrayType, SyntaxKind.ArrayType);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeCastExpression, SyntaxKind.CastExpression);
    }

    private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (VariableDeclarationSyntax)context.Node;
        var type = context.SemanticModel.GetTypeInfo(declaration.Type, context.CancellationToken).Type;
        if (type is null)
        {
            return;
        }

        if (!IsWithinUdonSharpScript(declaration, context))
        {
            return;
        }

        if (IsBlockedCollection(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(CollectionRule, declaration.Type.GetLocation(), type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        if (IsUnsupportedValueType(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedValueTypeRule, declaration.Type.GetLocation(), type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        if (!IsWithinUdonSharpScript(creation, context))
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (type is null)
        {
            return;
        }

        var displayName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (IsBlockedCollection(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(CollectionRule, creation.Type.GetLocation(), displayName));
        }
        else if (IsUnityDynamicType(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnityDynamicCreationRule, creation.Type.GetLocation(), displayName));
        }
        else if (IsUserDefinedReferenceType(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(UserTypeInstantiationRule, creation.Type.GetLocation()));
        }
    }

    private static void AnalyzeArrayType(SyntaxNodeAnalysisContext context)
    {
        var arrayType = (ArrayTypeSyntax)context.Node;
        if (!IsWithinUdonSharpScript(arrayType, context))
        {
            return;
        }

        foreach (var rankSpecifier in arrayType.RankSpecifiers)
        {
            if (rankSpecifier.Rank > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(MultiDimensionalArrayRule, rankSpecifier.GetLocation()));
                break;
            }
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsWithinUdonSharpScript(invocation, context))
        {
            return;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var name = memberAccess.Name.Identifier.Text;
            if (string.Equals(name, "AddComponent", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnityDynamicCreationRule, memberAccess.Name.GetLocation(), "AddComponent"));
            }
            else if (string.Equals(name, "Instantiate", StringComparison.Ordinal) || string.Equals(name, "CreatePrimitive", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnityDynamicCreationRule, memberAccess.Name.GetLocation(), name));
            }
        }
    }

    private static void AnalyzeCastExpression(SyntaxNodeAnalysisContext context)
    {
        var cast = (CastExpressionSyntax)context.Node;
        if (!IsWithinUdonSharpScript(cast, context))
        {
            return;
        }

        var targetType = context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type;
        var sourceType = context.SemanticModel.GetTypeInfo(cast.Expression, context.CancellationToken).Type;
        if (targetType is null || sourceType is null)
        {
            return;
        }

        if (IsNumericTypeAndNarrowing(sourceType, targetType))
        {
            context.ReportDiagnostic(Diagnostic.Create(NumericCastRule, cast.Type.GetLocation(), sourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static bool IsNumericTypeAndNarrowing(ITypeSymbol source, ITypeSymbol target)
    {
        if (!IsNumeric(source) || !IsNumeric(target))
        {
            return false;
        }

        var sourceRank = GetNumericRank(source);
        var targetRank = GetNumericRank(target);
        return sourceRank > targetRank;
    }

    private static int GetNumericRank(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_SByte => 1,
            SpecialType.System_Byte => 1,
            SpecialType.System_Int16 => 2,
            SpecialType.System_UInt16 => 2,
            SpecialType.System_Int32 => 3,
            SpecialType.System_UInt32 => 3,
            SpecialType.System_Int64 => 4,
            SpecialType.System_UInt64 => 4,
            SpecialType.System_Single => 5,
            SpecialType.System_Double => 6,
            SpecialType.System_Decimal => 7,
            _ => 0,
        };
    }

    private static bool IsNumeric(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal => true,
            _ => false,
        };
    }

    private static bool IsBlockedCollection(ITypeSymbol type)
    {
        var qualifiedName = Normalize(type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return BlockedCollections.Contains(qualifiedName);
    }

    private static bool IsUnityDynamicType(ITypeSymbol type)
    {
        var fullName = Normalize(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        if (UnityDynamicCreationTypes.Contains(fullName))
        {
            return true;
        }

        return type.BaseType is not null && IsUnityDynamicType(type.BaseType);
    }

    private static bool IsUserDefinedReferenceType(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class && type.Locations.Any(location => location.IsInSource);
    }

    private static bool IsUnsupportedValueType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Decimal)
        {
            return true;
        }

        var name = Normalize(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return string.Equals(name, "System.IntPtr", StringComparison.Ordinal)
            || string.Equals(name, "System.UIntPtr", StringComparison.Ordinal);
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

    private static string Normalize(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal) ? name["global::".Length..] : name;
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

