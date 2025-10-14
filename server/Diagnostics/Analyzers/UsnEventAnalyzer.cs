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
public sealed class UsnEventAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor EventSignatureRule = new(
        "USL030",
        "Udon event must be declared as public override",
        "The event '{0}' should be declared as 'public override'.",
        "UdonSharp.Events",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL030");

    private static readonly DiagnosticDescriptor DeprecatedEventRule = new(
        "USL031",
        "Deprecated Udon event signature",
        "The event '{0}' uses a deprecated signature.",
        "UdonSharp.Events",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL031");

    private static readonly DiagnosticDescriptor EventPrerequisiteRule = new(
        "USL032",
        "Event prerequisites",
        "The event '{0}' requires the associated component or collider to be present in the scene.",
        "UdonSharp.Events",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL032");

    private static readonly DiagnosticDescriptor LifecycleGuidanceRule = new(
        "USL033",
        "Lifecycle guidance",
        "'{0}' is not recommended in UdonSharp. Prefer using Start()/Update() patterns.",
        "UdonSharp.Events",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "udonsharp://rules/USL033");

    private static readonly ImmutableDictionary<string, EventSignature> ExpectedEvents = new Dictionary<string, EventSignature>(StringComparer.Ordinal)
    {
        ["Start"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["Update"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["LateUpdate"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["FixedUpdate"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["Interact"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["OnPlayerJoined"] = EventSignature.WithParameters(new[] { "VRC.SDKBase.VRCPlayerApi" }),
        ["OnPlayerLeft"] = EventSignature.WithParameters(new[] { "VRC.SDKBase.VRCPlayerApi" }),
        ["OnPickup"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["OnDrop"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["OnPickupUseDown"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["OnPickupUseUp"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["OnStationEntered"] = EventSignature.WithParameters(new[] { "VRC.SDKBase.VRCPlayerApi" }),
        ["OnStationExited"] = EventSignature.WithParameters(new[] { "VRC.SDKBase.VRCPlayerApi" }),
        ["OnVideoStart"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["OnVideoEnd"] = EventSignature.WithParameters(Array.Empty<string>()),
        ["OnPlayerTriggerEnter"] = EventSignature.WithParameters(new[] { "VRC.SDKBase.VRCPlayerApi" }),
        ["OnPlayerTriggerExit"] = EventSignature.WithParameters(new[] { "VRC.SDKBase.VRCPlayerApi" }),
    }.ToImmutableDictionary();

    private static readonly ImmutableHashSet<string> EventsWithPrerequisites = ImmutableHashSet.Create(StringComparer.Ordinal,
        "Interact",
        "OnPickup",
        "OnDrop",
        "OnPickupUseDown",
        "OnPickupUseUp",
        "OnStationEntered",
        "OnStationExited"
    );

    private static readonly ImmutableHashSet<string> DiscouragedLifecycleMethods = ImmutableHashSet.Create(StringComparer.Ordinal,
        "Awake",
        "OnEnable",
        "OnDisable"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(EventSignatureRule, DeprecatedEventRule, EventPrerequisiteRule, LifecycleGuidanceRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (methodSymbol?.ContainingType is null)
        {
            return;
        }

        if (!IsPotentialUdonScript(methodSymbol.ContainingType))
        {
            return;
        }

        var methodName = methodSymbol.Name;

        if (DiscouragedLifecycleMethods.Contains(methodName))
        {
            context.ReportDiagnostic(Diagnostic.Create(LifecycleGuidanceRule, methodDeclaration.Identifier.GetLocation(), methodName));
        }

        if (!ExpectedEvents.TryGetValue(methodName, out var eventSignature))
        {
            // Check for deprecated variants explicitly.
            if (string.Equals(methodName, "OnPlayerJoined", StringComparison.Ordinal) && methodSymbol.Parameters.Length == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(DeprecatedEventRule, methodDeclaration.Identifier.GetLocation(), methodName));
            }
            return;
        }

        if (!methodDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)) ||
            !methodDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.OverrideKeyword)))
        {
            context.ReportDiagnostic(Diagnostic.Create(EventSignatureRule, methodDeclaration.Identifier.GetLocation(), methodName));
        }

        if (!eventSignature.Matches(methodSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(DeprecatedEventRule, methodDeclaration.Identifier.GetLocation(), methodName));
        }

        if (EventsWithPrerequisites.Contains(methodName))
        {
            context.ReportDiagnostic(Diagnostic.Create(EventPrerequisiteRule, methodDeclaration.Identifier.GetLocation(), methodName));
        }
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

    private readonly record struct EventSignature(ImmutableArray<string> ParameterTypes)
    {
        public static EventSignature WithParameters(IReadOnlyList<string> parameters)
        {
            return new EventSignature(parameters.ToImmutableArray());
        }

        public bool Matches(IMethodSymbol method)
        {
            if (method.Parameters.Length != ParameterTypes.Length)
            {
                return false;
            }

            for (var index = 0; index < ParameterTypes.Length; index++)
            {
                var expected = ParameterTypes[index];
                var actual = method.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
