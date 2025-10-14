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
    public static bool IsUdonSharpBehaviour(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        while (type is not null)
        {
            if (string.Equals(type.Name, "UdonSharpBehaviour", StringComparison.Ordinal))
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

    public static AttributeData? GetAttribute(INamedTypeSymbol type, string attributeName)
    {
        return type
            .GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass?.Name?.Equals(attributeName, StringComparison.Ordinal) == true ||
                attribute.AttributeClass?.Name?.Equals($"{attributeName}Attribute", StringComparison.Ordinal) == true);
    }

    public static AttributeData? GetAttribute(ISymbol symbol, string attributeName)
    {
        return symbol
            .GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass?.Name?.Equals(attributeName, StringComparison.Ordinal) == true ||
                attribute.AttributeClass?.Name?.Equals($"{attributeName}Attribute", StringComparison.Ordinal) == true);
    }

    public static bool TryGetAttributeArgument<T>(AttributeData attribute, int index, out T value)
    {
        value = default!;

        if (attribute.ConstructorArguments.Length <= index)
        {
            return false;
        }

        var argument = attribute.ConstructorArguments[index];
        if (argument.Value is T typed)
        {
            value = typed;
            return true;
        }

        return false;
    }

    public static bool TryResolveStringLiteral(SemanticModel model, ExpressionSyntax expression, CancellationToken cancellationToken, out string value)
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

    public static ImmutableArray<IFieldSymbol> GetFieldsWithAttribute(INamedTypeSymbol type, string attributeName)
    {
        return type
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => field
                .GetAttributes()
                .Any(attribute =>
                    attribute.AttributeClass?.Name?.Equals(attributeName, StringComparison.Ordinal) == true ||
                    attribute.AttributeClass?.Name?.Equals($"{attributeName}Attribute", StringComparison.Ordinal) == true))
            .ToImmutableArray();
    }

    public static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        return symbol
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.Name?.Equals(attributeName, StringComparison.Ordinal) == true ||
                attribute.AttributeClass?.Name?.Equals($"{attributeName}Attribute", StringComparison.Ordinal) == true);
    }

    public static bool IsBehaviourSyncMode(INamedTypeSymbol behaviour, string modeName)
    {
        var attribute = GetAttribute(behaviour, "UdonBehaviourSyncMode");
        if (attribute is null)
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length == 0)
        {
            return false;
        }

        var argument = attribute.ConstructorArguments[0];
        var value = argument.Value?.ToString();
        return string.Equals(value, modeName, StringComparison.Ordinal);
    }

    public static string? GetBehaviourSyncMode(INamedTypeSymbol behaviour)
    {
        var attribute = GetAttribute(behaviour, "UdonBehaviourSyncMode");
        if (attribute is null || attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        return attribute.ConstructorArguments[0].Value?.ToString();
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

    public static bool IsStringLiteral(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.StringLiteralExpression);
    }
}
