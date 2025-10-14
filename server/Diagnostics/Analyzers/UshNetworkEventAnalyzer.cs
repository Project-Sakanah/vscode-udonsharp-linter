using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshNetworkEventAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0001,
        UshRuleDescriptors.Ush0002,
        UshRuleDescriptors.Ush0003,
        UshRuleDescriptors.Ush0004,
        UshRuleDescriptors.Ush0005,
        UshRuleDescriptors.Ush0006,
        UshRuleDescriptors.Ush0043);

    private static readonly HashSet<string> CustomEventMethodNames = new(StringComparer.Ordinal)
    {
        "SendCustomEvent",
        "SendCustomEventDelayedFrames",
        "SendCustomEventDelayedSeconds"
    };

    private static readonly HashSet<string> NetworkEventMethodNames = new(StringComparer.Ordinal)
    {
        "SendCustomNetworkEvent",
        "SendCustomNetworkEventDelayedFrames",
        "SendCustomNetworkEventDelayedSeconds"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!TryIdentifyEventInvocation(invocation, context.SemanticModel, context.CancellationToken, out var methodSymbol, out var methodName, out var isCustomEvent, out var isNetworkEvent))
        {
            return;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            return;
        }

        var eventArgumentIndex = isNetworkEvent ? 1 : 0;
        if (arguments.Count <= eventArgumentIndex)
        {
            return;
        }

        var eventArgument = arguments[eventArgumentIndex];
        if (!UshAnalyzerUtilities.TryResolveEventTarget(context.SemanticModel, eventArgument.Expression, context.CancellationToken, out var targetName, out var resolvedMethodSymbol))
        {
            return;
        }

        var targetBehaviour = DetermineTargetBehaviour(invocation, methodSymbol, resolvedMethodSymbol, context.SemanticModel, context.CancellationToken);
        if (targetBehaviour is null)
        {
            return;
        }

        if (!UshAnalyzerUtilities.IsUdonSharpBehaviour(targetBehaviour))
        {
            return;
        }

        if (resolvedMethodSymbol?.ContainingType is INamedTypeSymbol resolvedContaining &&
            UshAnalyzerUtilities.IsUdonSharpBehaviour(resolvedContaining))
        {
            targetBehaviour = resolvedContaining;
        }

        var methodSet = new HashSet<IMethodSymbol>(MethodSymbolComparer.Instance);
        foreach (var method in UshAnalyzerUtilities.GetBehaviourMethods(targetBehaviour, targetName))
        {
            methodSet.Add(method);
        }

        if (resolvedMethodSymbol is not null &&
            resolvedMethodSymbol.ContainingType?.Equals(targetBehaviour, SymbolEqualityComparer.Default) == true)
        {
            methodSet.Add(resolvedMethodSymbol);
        }

        var candidateMethods = ImmutableArray.CreateRange(methodSet);

        ValidateTargetExistence(context, eventArgument, targetName, candidateMethods);
        ValidateTargetAccessibility(context, eventArgument, targetName, candidateMethods);

        if (isNetworkEvent)
        {
            ValidateNetworkNaming(context, eventArgument, targetName);
            ValidateNetworkCallableAttribute(context, arguments, eventArgument, candidateMethods, targetName, context.CancellationToken);
            ValidateNetworkParameterTypes(context, invocation, arguments, candidateMethods, targetName);
            ValidateNetworkSyncMode(context, invocation, targetBehaviour, context.SemanticModel, context.CancellationToken);
        }

        if (UshAnalyzerUtilities.IsStringLiteral(eventArgument.Expression))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0043,
                eventArgument.GetLocation(),
                targetName));
        }
    }

    private static void ValidateTargetExistence(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax eventArgument,
        string methodName,
        ImmutableArray<IMethodSymbol> candidateMethods)
    {
        if (candidateMethods.IsDefaultOrEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0001,
                eventArgument.GetLocation(),
                methodName));
        }
    }

    private static void ValidateTargetAccessibility(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax eventArgument,
        string methodName,
        ImmutableArray<IMethodSymbol> candidateMethods)
    {
        foreach (var method in candidateMethods)
        {
            if (method.DeclaredAccessibility != Accessibility.Public)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0002,
                    eventArgument.GetLocation(),
                    methodName));
                return;
            }
        }
    }

    private static void ValidateNetworkNaming(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax eventArgument,
        string methodName)
    {
        if (methodName.StartsWith("_", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0003,
                eventArgument.GetLocation(),
                methodName));
        }
    }

    private static void ValidateNetworkCallableAttribute(
        SyntaxNodeAnalysisContext context,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ArgumentSyntax eventArgument,
        ImmutableArray<IMethodSymbol> candidateMethods,
        string methodName,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= 2)
        {
            return;
        }

        if (candidateMethods.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var method in candidateMethods)
        {
            if (UshAnalyzerUtilities.HasNetworkCallableAttribute(method, cancellationToken))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            UshRuleDescriptors.Ush0004,
            eventArgument.GetLocation(),
            methodName));
    }

    private static void ValidateNetworkParameterTypes(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ImmutableArray<IMethodSymbol> candidateMethods,
        string methodName)
    {
        if (arguments.Count <= 2)
        {
            return;
        }

        var payloadCount = arguments.Count - 2;
        var candidate = candidateMethods.FirstOrDefault(method => method.Parameters.Length == payloadCount);

        if (candidate is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0005,
                invocation.GetLocation(),
                0,
                methodName));
            return;
        }

        for (var index = 0; index < payloadCount; index++)
        {
            var argumentExpression = arguments[index + 2].Expression;
            var parameterType = candidate.Parameters[index].Type;
            var conversion = context.SemanticModel.ClassifyConversion(argumentExpression, parameterType);

            if (!conversion.IsIdentity && !conversion.IsImplicit)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0005,
                    argumentExpression.GetLocation(),
                    index + 1,
                    methodName));
                return;
            }
        }
    }

    private static void ValidateNetworkSyncMode(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol targetBehaviour,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (UshAnalyzerUtilities.IsBehaviourSyncMode(targetBehaviour, "None", semanticModel, cancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0006,
                invocation.GetLocation(),
                targetBehaviour.Name));
        }
    }

    private static bool TryIdentifyEventInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol? methodSymbol,
        out string methodName,
        out bool isCustomEvent,
        out bool isNetworkEvent)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (methodSymbol is not null)
        {
            methodName = methodSymbol.Name;
        }
        else
        {
            methodName = GetMethodNameFromExpression(invocation.Expression) ?? string.Empty;
        }

        isCustomEvent = CustomEventMethodNames.Contains(methodName);
        isNetworkEvent = NetworkEventMethodNames.Contains(methodName);
        return isCustomEvent || isNetworkEvent;
    }

    private static string? GetMethodNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => GetSimpleName(memberAccess.Name),
            MemberBindingExpressionSyntax memberBinding => GetSimpleName(memberBinding.Name),
            ConditionalAccessExpressionSyntax conditional => GetMethodNameFromExpression(conditional.WhenNotNull),
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => null,
        };
    }

    private static string? GetSimpleName(SimpleNameSyntax nameSyntax)
    {
        return nameSyntax.Identifier.Text;
    }

    private static INamedTypeSymbol? ResolveBehaviourFromMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionType = UshAnalyzerUtilities.GetExpressionType(semanticModel, memberAccess.Expression, cancellationToken);
        if (expressionType is INamedTypeSymbol named && UshAnalyzerUtilities.IsUdonSharpBehaviour(named))
        {
            return named;
        }

        var symbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        switch (symbol)
        {
            case ILocalSymbol local when UshAnalyzerUtilities.IsUdonSharpBehaviour(local.Type as INamedTypeSymbol):
                return (INamedTypeSymbol)local.Type;
            case IFieldSymbol field when UshAnalyzerUtilities.IsUdonSharpBehaviour(field.Type as INamedTypeSymbol):
                return (INamedTypeSymbol)field.Type;
            case IPropertySymbol property when UshAnalyzerUtilities.IsUdonSharpBehaviour(property.Type as INamedTypeSymbol):
                return (INamedTypeSymbol)property.Type;
        }

        return expressionType as INamedTypeSymbol;
    }

    private static INamedTypeSymbol? DetermineTargetBehaviour(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol,
        IMethodSymbol? resolvedMethodSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (resolvedMethodSymbol?.ContainingType is INamedTypeSymbol containing &&
            UshAnalyzerUtilities.IsUdonSharpBehaviour(containing))
        {
            return containing;
        }

        if (methodSymbol is null)
        {
            return null;
        }

        return ResolveTargetBehaviour(invocation, methodSymbol, semanticModel, cancellationToken);
    }

    private static INamedTypeSymbol? ResolveTargetBehaviour(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var type = ResolveBehaviourFromMemberAccess(memberAccess, semanticModel, cancellationToken);
            if (type is not null)
            {
                return type;
            }
        }

        var containingType = UshAnalyzerUtilities.GetContainingType(invocation, semanticModel, cancellationToken);
        if (containingType is not null)
        {
            return containingType;
        }

        return symbol.ContainingType as INamedTypeSymbol;
    }

    private sealed class MethodSymbolComparer : IEqualityComparer<IMethodSymbol>
    {
        public static readonly MethodSymbolComparer Instance = new();

        public bool Equals(IMethodSymbol? x, IMethodSymbol? y)
        {
            return SymbolEqualityComparer.Default.Equals(x, y);
        }

        public int GetHashCode(IMethodSymbol obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj);
        }
    }
}
