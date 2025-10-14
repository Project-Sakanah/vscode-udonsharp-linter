using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpLsp.Server.Diagnostics;

internal static class UshAnalyzerUtilities
{
    private const string FieldChangeCallbackMetadataName = "VRC.Udon.Serialization.Attributes.FieldChangeCallbackAttribute";
    private const string NetworkCallableMetadataName = "VRC.SDKBase.Attributes.NetworkCallableAttribute";
    private const string UdonSyncedMetadataName = "UdonSharp.UdonSyncedAttribute";
    private const string UdonBehaviourSyncModeMetadataName = "UdonSharp.UdonBehaviourSyncModeAttribute";

    // ---------------------------------------------------------------------
    // Core symbol helpers
    // ---------------------------------------------------------------------

    public static bool IsUdonSharpBehaviour(INamedTypeSymbol? type)
    {
        while (type is not null)
        {
            if (string.Equals(type.Name, "UdonSharpBehaviour", StringComparison.Ordinal))
            {
                return true;
            }

            if (DeclaresUdonSharpBehaviour(type))
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    public static INamedTypeSymbol? GetContainingType(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var declaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        return declaration is null
            ? null
            : semanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
    }

    private static bool DeclaresUdonSharpBehaviour(INamedTypeSymbol type)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not TypeDeclarationSyntax declaration || declaration.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in declaration.BaseList.Types)
            {
                var baseName = GetSimpleTypeName(baseType.Type);
                if (string.Equals(baseName, "UdonSharpBehaviour", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => typeSyntax.ToString()
        };
    }

    public static AttributeData? GetAttribute(
        ISymbol symbol,
        string simpleName,
        string? metadataName = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (MatchesAttribute(attribute, simpleName, metadataName, cancellationToken))
            {
                return attribute;
            }
        }

        return null;
    }

    public static bool HasAttribute(
        ISymbol symbol,
        string simpleName,
        string? metadataName = null,
        CancellationToken cancellationToken = default)
    {
        return symbol
            .GetAttributes()
            .Any(attribute => MatchesAttribute(attribute, simpleName, metadataName, cancellationToken));
    }

    public static IEnumerable<AttributeData> GetAttributes(
        ISymbol symbol,
        string metadataName,
        CancellationToken cancellationToken = default)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (MatchesAttribute(attribute, attribute.AttributeClass?.Name ?? string.Empty, metadataName, cancellationToken))
            {
                yield return attribute;
            }
        }
    }

    private static bool MatchesAttribute(
        AttributeData attribute,
        string simpleName,
        string? metadataName,
        CancellationToken cancellationToken)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is not null)
        {
            if (!string.IsNullOrEmpty(metadataName))
            {
                var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (string.Equals(fullName, $"global::{metadataName}", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (string.Equals(attributeClass.Name, simpleName, StringComparison.Ordinal) ||
                string.Equals(attributeClass.Name, $"{simpleName}Attribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
        {
            var syntaxName = GetSimpleName(attributeSyntax.Name);
            if (string.Equals(syntaxName, simpleName, StringComparison.Ordinal) ||
                string.Equals(syntaxName, $"{simpleName}Attribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetSimpleName(NameSyntax nameSyntax)
    {
        return nameSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => nameSyntax.ToString()
        };
    }

    // ---------------------------------------------------------------------
    // Attribute helpers
    // ---------------------------------------------------------------------

    public static AttributeData? GetUdonSyncedAttribute(IFieldSymbol field, CancellationToken cancellationToken)
    {
        return GetAttribute(field, "UdonSynced", UdonSyncedMetadataName, cancellationToken);
    }

    public static AttributeData? GetBehaviourSyncModeAttribute(INamedTypeSymbol behaviour, CancellationToken cancellationToken)
    {
        return GetAttribute(behaviour, "UdonBehaviourSyncMode", UdonBehaviourSyncModeMetadataName, cancellationToken);
    }

    public static AttributeData? GetFieldChangeCallbackAttribute(IFieldSymbol field, CancellationToken cancellationToken)
    {
        return GetAttribute(field, "FieldChangeCallback", FieldChangeCallbackMetadataName, cancellationToken);
    }

    public static bool HasNetworkCallableAttribute(ISymbol method, CancellationToken cancellationToken)
    {
        return HasAttribute(method, "NetworkCallable", NetworkCallableMetadataName, cancellationToken);
    }

    public static string? GetBehaviourSyncModeName(
        INamedTypeSymbol behaviour,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var attribute = GetBehaviourSyncModeAttribute(behaviour, cancellationToken);
        if (attribute is null)
        {
            return null;
        }

        return GetEnumNameFromAttribute(attribute, semanticModel, cancellationToken, positionalIndex: 0, namedArgumentKey: "Mode");
    }

    public static bool IsBehaviourSyncMode(
        INamedTypeSymbol behaviour,
        string expectedMode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var mode = GetBehaviourSyncModeName(behaviour, semanticModel, cancellationToken);
        return string.Equals(mode, expectedMode, StringComparison.Ordinal);
    }

    public static string? GetUdonSyncModeName(
        AttributeData attribute,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return GetEnumNameFromAttribute(attribute, semanticModel, cancellationToken, positionalIndex: 0, namedArgumentKey: "SyncMode");
    }

    private static string? GetEnumNameFromAttribute(
        AttributeData attribute,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        int positionalIndex,
        string? namedArgumentKey)
    {
        var semanticValue = GetEnumAttributeValue(attribute, positionalIndex, namedArgumentKey);
        if (!string.IsNullOrEmpty(semanticValue))
        {
            return semanticValue;
        }

        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax attributeSyntax)
        {
            return null;
        }

        AttributeArgumentSyntax? targetArgument = null;
        if (!string.IsNullOrEmpty(namedArgumentKey))
        {
            targetArgument = attributeSyntax.ArgumentList?.Arguments
                .FirstOrDefault(argument => string.Equals(argument.NameEquals?.Name.Identifier.Text, namedArgumentKey, StringComparison.Ordinal));
        }

        if (targetArgument is null && attributeSyntax.ArgumentList is { Arguments.Count: > 0 })
        {
            if (positionalIndex < attributeSyntax.ArgumentList.Arguments.Count)
            {
                targetArgument = attributeSyntax.ArgumentList.Arguments[positionalIndex];
            }
        }

        if (targetArgument is null)
        {
            return null;
        }

        var expression = targetArgument.Expression;
        var model = semanticModel.Compilation.GetSemanticModel(expression.SyntaxTree);
        var symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is IFieldSymbol field && field.ContainingType?.TypeKind == TypeKind.Enum)
        {
            return field.Name;
        }

        return ExtractEnumNameFromExpression(expression);
    }

    private static string? GetEnumAttributeValue(AttributeData attribute, int constructorIndex, string? namedArgumentKey)
    {
        if (attribute.ConstructorArguments.Length > constructorIndex)
        {
            var name = GetEnumConstantName(attribute.ConstructorArguments[constructorIndex]);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        if (!string.IsNullOrEmpty(namedArgumentKey))
        {
            foreach (var (key, constant) in attribute.NamedArguments)
            {
                if (string.Equals(key, namedArgumentKey, StringComparison.Ordinal))
                {
                    return GetEnumConstantName(constant);
                }
            }
        }

        return null;
    }

    private static string? GetEnumConstantName(TypedConstant constant)
    {
        if (constant.Type is INamedTypeSymbol enumType && enumType.TypeKind == TypeKind.Enum)
        {
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue && Equals(member.ConstantValue, constant.Value))
                {
                    return member.Name;
                }
            }
        }

        if (constant.Value is Enum enumValue)
        {
            return enumValue.ToString();
        }

        return null;
    }

    private static string? ExtractEnumNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => null
        };
    }

    // ---------------------------------------------------------------------
    // Constant helpers
    // ---------------------------------------------------------------------

    public static bool TryResolveConstantString(
        SemanticModel model,
        ExpressionSyntax expression,
        CancellationToken cancellationToken,
        out string value)
    {
        if (TryGetConstantValue(model, expression, cancellationToken, out value))
        {
            return true;
        }

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.Text, "nameof", StringComparison.Ordinal) &&
            invocation.ArgumentList.Arguments.Count == 1)
        {
            var argumentExpression = invocation.ArgumentList.Arguments[0].Expression;
            var symbol = model.GetSymbolInfo(argumentExpression, cancellationToken).Symbol;
            if (symbol is not null)
            {
                value = symbol.Name;
                return true;
            }

            value = argumentExpression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                _ => argumentExpression.ToString()
            };
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetConstantValue(
        SemanticModel model,
        ExpressionSyntax expression,
        CancellationToken cancellationToken,
        out string value)
    {
        value = string.Empty;
        var constant = model.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue && constant.Value is string text)
        {
            value = text;
            return true;
        }

        return false;
    }

    public static bool IsStringLiteral(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.StringLiteralExpression);
    }

    public static bool TryResolveEventTarget(
        SemanticModel model,
        ExpressionSyntax expression,
        CancellationToken cancellationToken,
        out string methodName,
        out IMethodSymbol? methodSymbol)
    {
        methodName = string.Empty;
        methodSymbol = null;

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.Text, "nameof", StringComparison.Ordinal) &&
            invocation.ArgumentList.Arguments.Count == 1)
        {
            var targetExpression = invocation.ArgumentList.Arguments[0].Expression;
            var symbolInfo = model.GetSymbolInfo(targetExpression, cancellationToken).Symbol;
            if (symbolInfo is IMethodSymbol method)
            {
                methodSymbol = method;
                methodName = method.Name;
                return true;
            }

            if (TryResolveConstantString(model, targetExpression, cancellationToken, out var inferredName))
            {
                methodName = inferredName;
                return true;
            }

            methodName = targetExpression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
                _ => targetExpression.ToString()
            };
            return true;
        }

        if (TryResolveConstantString(model, expression, cancellationToken, out var literalName))
        {
            methodName = literalName;
            return true;
        }

        return false;
    }

    public static bool TryGetAttributeStringArgument(
        AttributeData attribute,
        AttributeSyntax? attributeSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string value)
    {
        value = string.Empty;

        if (attribute.ConstructorArguments.Length > 0)
        {
            var argument = attribute.ConstructorArguments[0];
            if (argument.Value is string text && !string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }

        if (attributeSyntax?.ArgumentList?.Arguments.Count > 0)
        {
            var expression = attributeSyntax.ArgumentList.Arguments[0].Expression;
            var model = semanticModel.Compilation.GetSemanticModel(expression.SyntaxTree);
            if (TryResolveConstantString(model, expression, cancellationToken, out var extracted) && !string.IsNullOrWhiteSpace(extracted))
            {
                value = extracted;
                return true;
            }
        }

        return false;
    }

    public static string GetFullyQualifiedName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static IEnumerable<IMethodSymbol> GetBehaviourMethods(INamedTypeSymbol behaviour, string methodName)
    {
        var current = behaviour;
        while (current is not null)
        {
            foreach (var member in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                yield return member;
            }

            current = current.BaseType;
        }
    }

    public static ITypeSymbol? GetExpressionType(SemanticModel model, ExpressionSyntax expression, CancellationToken cancellationToken)
    {
        var typeInfo = model.GetTypeInfo(expression, cancellationToken);
        return typeInfo.Type ?? typeInfo.ConvertedType;
    }
}
