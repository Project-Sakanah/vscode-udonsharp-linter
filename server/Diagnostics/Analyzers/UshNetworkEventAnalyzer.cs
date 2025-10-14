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
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null)
        {
            return;
        }

        var methodName = symbol.Name;
        var isCustomEvent = CustomEventMethodNames.Contains(methodName);
        var isNetworkEvent = NetworkEventMethodNames.Contains(methodName);

        if (!isCustomEvent && !isNetworkEvent)
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
        if (!UshAnalyzerUtilities.TryResolveStringLiteral(context.SemanticModel, eventArgument.Expression, context.CancellationToken, out var targetName))
        {
            return;
        }

        var targetBehaviour = ResolveTargetBehaviour(invocation, symbol, context.SemanticModel, context.CancellationToken);
        if (targetBehaviour is null || !UshAnalyzerUtilities.IsUdonSharpBehaviour(targetBehaviour))
        {
            return;
        }

        ValidateTargetExistence(context, eventArgument, targetBehaviour, targetName);
        ValidateTargetAccessibility(context, eventArgument, targetBehaviour, targetName);

        if (isNetworkEvent)
        {
            ValidateNetworkNaming(context, eventArgument, targetName);
            ValidateNetworkCallableAttribute(context, invocation, arguments, eventArgument, targetBehaviour, targetName);
            ValidateNetworkParameterTypes(context, invocation, arguments, targetBehaviour, targetName);
            ValidateNetworkSyncMode(context, invocation, targetBehaviour);
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
        INamedTypeSymbol behaviour,
        string methodName)
    {
        if (!UshAnalyzerUtilities.GetBehaviourMethods(behaviour, methodName).Any())
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
        INamedTypeSymbol behaviour,
        string methodName)
    {
        foreach (var method in UshAnalyzerUtilities.GetBehaviourMethods(behaviour, methodName))
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
        InvocationExpressionSyntax invocation,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ArgumentSyntax eventArgument,
        INamedTypeSymbol behaviour,
        string methodName)
    {
        if (arguments.Count <= 2)
        {
            return;
        }

        foreach (var method in UshAnalyzerUtilities.GetBehaviourMethods(behaviour, methodName))
        {
            if (UshAnalyzerUtilities.HasAttribute(method, "NetworkCallable"))
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
        INamedTypeSymbol behaviour,
        string methodName)
    {
        if (arguments.Count <= 2)
        {
            return;
        }

        var payloadCount = arguments.Count - 2;
        var candidate = UshAnalyzerUtilities
            .GetBehaviourMethods(behaviour, methodName)
            .FirstOrDefault(method => method.Parameters.Length == payloadCount);

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
        INamedTypeSymbol targetBehaviour)
    {
        if (UshAnalyzerUtilities.IsBehaviourSyncMode(targetBehaviour, "BehaviourSyncMode.None"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0006,
                invocation.GetLocation(),
                targetBehaviour.Name));
        }
    }

    private static INamedTypeSymbol? ResolveTargetBehaviour(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var type = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type as INamedTypeSymbol;
            if (type is not null)
            {
                return type;
            }
        }

        return symbol.ContainingType as INamedTypeSymbol
            ?? UshAnalyzerUtilities.GetContainingType(invocation, semanticModel, cancellationToken);
    }
}
