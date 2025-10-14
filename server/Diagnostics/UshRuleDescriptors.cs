using Microsoft.CodeAnalysis;

namespace UdonSharpLsp.Server.Diagnostics;

internal static class UshRuleDescriptors
{
    private const string HelpLinkPrefix = "udonsharp://rules/";

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string messageFormat,
        string category,
        DiagnosticSeverity severity)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            category,
            severity,
            isEnabledByDefault: true,
            helpLinkUri: $"{HelpLinkPrefix}{id}");
    }

    // USH0001: Ensure SendCustomEvent targets exist.
    public static readonly DiagnosticDescriptor Ush0001 = Create(
        "USH0001",
        "Custom event target is missing",
        "The target behaviour does not declare a method named '{0}'.",
        "Network",
        DiagnosticSeverity.Error);

    // USH0002: Custom event targets must be public.
    public static readonly DiagnosticDescriptor Ush0002 = Create(
        "USH0002",
        "Custom event target must be public",
        "The method '{0}' must be declared as public to be invoked via custom events.",
        "Network",
        DiagnosticSeverity.Error);

    // USH0003: Network callable methods may not start with underscore.
    public static readonly DiagnosticDescriptor Ush0003 = Create(
        "USH0003",
        "Network event names cannot start with an underscore",
        "The network callable method '{0}' cannot start with an underscore.",
        "Network",
        DiagnosticSeverity.Error);

    // USH0004: Methods with network parameters require [NetworkCallable].
    public static readonly DiagnosticDescriptor Ush0004 = Create(
        "USH0004",
        "Network callable methods with parameters require [NetworkCallable]",
        "The method '{0}' must be annotated with [NetworkCallable] when invoked with parameters over the network.",
        "Network",
        DiagnosticSeverity.Error);

    // USH0005: Network method signatures must match argument types.
    public static readonly DiagnosticDescriptor Ush0005 = Create(
        "USH0005",
        "Network method parameter mismatch",
        "Argument #{0} for '{1}' does not match the declared parameter type.",
        "Network",
        DiagnosticSeverity.Error);

    // USH0006: Cannot send network events to BehaviourSyncMode.None.
    public static readonly DiagnosticDescriptor Ush0006 = Create(
        "USH0006",
        "Cannot send network events to behaviours with SyncType.None",
        "The target behaviour '{0}' uses BehaviourSyncMode.None and cannot receive network events.",
        "Network",
        DiagnosticSeverity.Error);

    // USH0007: BehaviourSyncMode.NoVariableSync disallows [UdonSynced].
    public static readonly DiagnosticDescriptor Ush0007 = Create(
        "USH0007",
        "NoVariableSync behaviours cannot declare synced variables",
        "Behaviours using BehaviourSyncMode.NoVariableSync cannot declare the synced field '{0}'.",
        "Synchronization",
        DiagnosticSeverity.Error);

    // USH0008: Only supported types may be synced.
    public static readonly DiagnosticDescriptor Ush0008 = Create(
        "USH0008",
        "Unsupported synced variable type",
        "The field '{0}' uses the unsupported synced type '{1}'.",
        "Synchronization",
        DiagnosticSeverity.Error);

    // USH0009: Synced arrays require BehaviourSyncMode.Manual.
    public static readonly DiagnosticDescriptor Ush0009 = Create(
        "USH0009",
        "Synced arrays require BehaviourSyncMode.Manual",
        "The synced array '{0}' requires the behaviour to use BehaviourSyncMode.Manual.",
        "Synchronization",
        DiagnosticSeverity.Error);

    // USH0010: Manual sync mode disallows tweening.
    public static readonly DiagnosticDescriptor Ush0010 = Create(
        "USH0010",
        "Manual sync mode does not support Linear/Smooth tweening",
        "BehaviourSyncMode.Manual cannot be combined with the sync mode '{0}' on field '{1}'.",
        "Synchronization",
        DiagnosticSeverity.Error);

    // USH0011: Linear interpolation supported type check.
    public static readonly DiagnosticDescriptor Ush0011 = Create(
        "USH0011",
        "Unsupported Linear interpolation type",
        "UdonSyncMode.Linear cannot be applied to the type '{0}'.",
        "Synchronization",
        DiagnosticSeverity.Error);

    // USH0012: Smooth interpolation supported type check.
    public static readonly DiagnosticDescriptor Ush0012 = Create(
        "USH0012",
        "Unsupported Smooth interpolation type",
        "UdonSyncMode.Smooth cannot be applied to the type '{0}'.",
        "Synchronization",
        DiagnosticSeverity.Error);

    // USH0013: Block forbidden method usage.
    public static readonly DiagnosticDescriptor Ush0013 = Create(
        "USH0013",
        "Method is not exposed to Udon",
        "The method '{0}' is not exposed to Udon and cannot be used.",
        "UdonApi",
        DiagnosticSeverity.Error);

    // USH0014: Block forbidden field/property usage.
    public static readonly DiagnosticDescriptor Ush0014 = Create(
        "USH0014",
        "Member is not exposed to Udon",
        "The member '{0}' is not exposed to Udon and cannot be accessed.",
        "UdonApi",
        DiagnosticSeverity.Error);

    // USH0015: Block forbidden types.
    public static readonly DiagnosticDescriptor Ush0015 = Create(
        "USH0015",
        "Type is not exposed to Udon",
        "The type '{0}' is not exposed to Udon and cannot be used.",
        "UdonApi",
        DiagnosticSeverity.Error);

    // USH0016: Enforce VRCPlayerApi event signatures.
    public static readonly DiagnosticDescriptor Ush0016 = Create(
        "USH0016",
        "Deprecated event signature",
        "The event '{0}' must use the VRCPlayerApi signature.",
        "Runtime",
        DiagnosticSeverity.Error);

    // USH0017: Instantiate only supports GameObject.
    public static readonly DiagnosticDescriptor Ush0017 = Create(
        "USH0017",
        "Instantiate only supports GameObject",
        "Only UnityEngine.GameObject can be instantiated at runtime in UdonSharp.",
        "Runtime",
        DiagnosticSeverity.Error);

    // USH0018: 'is' keyword unsupported.
    public static readonly DiagnosticDescriptor Ush0018 = Create(
        "USH0018",
        "'is' keyword is not supported in UdonSharp",
        "The 'is' keyword is not supported in UdonSharp scripts.",
        "Runtime",
        DiagnosticSeverity.Error);

    // USH0019: 'as' keyword unsupported.
    public static readonly DiagnosticDescriptor Ush0019 = Create(
        "USH0019",
        "'as' keyword is not supported in UdonSharp",
        "The 'as' keyword is not supported in UdonSharp scripts.",
        "Runtime",
        DiagnosticSeverity.Error);

    // USH0020: try/catch/finally unsupported.
    public static readonly DiagnosticDescriptor Ush0020 = Create(
        "USH0020",
        "try/catch/finally is not supported",
        "Exception handling constructs are not supported in UdonSharp scripts.",
        "Runtime",
        DiagnosticSeverity.Error);

    // USH0021: throw unsupported.
    public static readonly DiagnosticDescriptor Ush0021 = Create(
        "USH0021",
        "'throw' is not supported",
        "Throwing exceptions is not supported in UdonSharp scripts.",
        "Runtime",
        DiagnosticSeverity.Error);

    // USH0022: Nullable types unsupported.
    public static readonly DiagnosticDescriptor Ush0022 = Create(
        "USH0022",
        "Nullable value types are not supported",
        "Nullable value types (T?) are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0023: Null-conditional operators unsupported.
    public static readonly DiagnosticDescriptor Ush0023 = Create(
        "USH0023",
        "Null-conditional operators are not supported",
        "Null-conditional operators (?. or ?[]) are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0024: Multidimensional array creation unsupported.
    public static readonly DiagnosticDescriptor Ush0024 = Create(
        "USH0024",
        "Multidimensional arrays are not supported",
        "Multidimensional array creation is not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0025: Multidimensional array access unsupported.
    public static readonly DiagnosticDescriptor Ush0025 = Create(
        "USH0025",
        "Multidimensional array access is not supported",
        "Accessing multidimensional arrays is not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0026: Local functions unsupported.
    public static readonly DiagnosticDescriptor Ush0026 = Create(
        "USH0026",
        "Local functions are not supported",
        "Local function declarations are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0027: Nested types unsupported.
    public static readonly DiagnosticDescriptor Ush0027 = Create(
        "USH0027",
        "Nested type declarations are not supported",
        "Nested type declarations are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0028: Constructors unsupported.
    public static readonly DiagnosticDescriptor Ush0028 = Create(
        "USH0028",
        "Constructors are not supported",
        "User-defined constructors are not supported in UdonSharp behaviours.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0029: Generic methods unsupported.
    public static readonly DiagnosticDescriptor Ush0029 = Create(
        "USH0029",
        "Generic methods are not supported",
        "Generic method declarations are not supported in UdonSharp behaviours.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0030: Interface implementation unsupported.
    public static readonly DiagnosticDescriptor Ush0030 = Create(
        "USH0030",
        "Interfaces are not supported",
        "Implementing interfaces is not supported in UdonSharp behaviours.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0031: Base method hiding unsupported.
    public static readonly DiagnosticDescriptor Ush0031 = Create(
        "USH0031",
        "Hiding base methods is not supported",
        "Methods in UdonSharp behaviours may not hide base class members with the same signature.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0032: Initializer lists unsupported.
    public static readonly DiagnosticDescriptor Ush0032 = Create(
        "USH0032",
        "Object and collection initializers are not supported",
        "Object or collection initializer expressions are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0033: typeof on user defined types unsupported.
    public static readonly DiagnosticDescriptor Ush0033 = Create(
        "USH0033",
        "typeof on user-defined types is not supported",
        "Using typeof on user-defined UdonSharp types is not supported.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0034: Static fields on user types unsupported.
    public static readonly DiagnosticDescriptor Ush0034 = Create(
        "USH0034",
        "Static members are not supported on UdonSharp behaviours",
        "Static fields or properties are not supported on UdonSharp behaviour scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0035: Partial methods unsupported.
    public static readonly DiagnosticDescriptor Ush0035 = Create(
        "USH0035",
        "Partial methods are not supported",
        "Partial method declarations are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0036: goto statement unsupported.
    public static readonly DiagnosticDescriptor Ush0036 = Create(
        "USH0036",
        "goto statements are not supported",
        "goto statements are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0037: Labeled statements unsupported.
    public static readonly DiagnosticDescriptor Ush0037 = Create(
        "USH0037",
        "Labeled statements are not supported",
        "Labeled statements are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0038: goto case unsupported.
    public static readonly DiagnosticDescriptor Ush0038 = Create(
        "USH0038",
        "goto case statements are not supported",
        "goto case statements are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0039: goto default unsupported.
    public static readonly DiagnosticDescriptor Ush0039 = Create(
        "USH0039",
        "goto default statements are not supported",
        "goto default statements are not supported in UdonSharp scripts.",
        "Language",
        DiagnosticSeverity.Error);

    // USH0040: Duplicate FieldChangeCallback targets not allowed.
    public static readonly DiagnosticDescriptor Ush0040 = Create(
        "USH0040",
        "Duplicate FieldChangeCallback target",
        "The FieldChangeCallback target '{0}' is already assigned to another field.",
        "Attributes",
        DiagnosticSeverity.Error);

    // USH0041: FieldChangeCallback requires matching property.
    public static readonly DiagnosticDescriptor Ush0041 = Create(
        "USH0041",
        "FieldChangeCallback target not found",
        "The property '{0}' referenced by FieldChangeCallback does not exist.",
        "Attributes",
        DiagnosticSeverity.Error);

    // USH0042: FieldChangeCallback property type mismatch.
    public static readonly DiagnosticDescriptor Ush0042 = Create(
        "USH0042",
        "FieldChangeCallback type mismatch",
        "The property '{0}' type must match the field '{1}' type.",
        "Attributes",
        DiagnosticSeverity.Error);

    // USH0043: Use nameof for event targets.
    public static readonly DiagnosticDescriptor Ush0043 = Create(
        "USH0043",
        "Prefer nameof for event identifiers",
        "Use nameof({0}) instead of a string literal when referencing event targets.",
        "BestPractices",
        DiagnosticSeverity.Warning);

    // USH0044: Require namespace declarations.
    public static readonly DiagnosticDescriptor Ush0044 = Create(
        "USH0044",
        "Scripts must declare a namespace",
        "Declare a namespace to avoid collisions in UdonSharp scripts.",
        "Structure",
        DiagnosticSeverity.Warning);

    // USH0045: Class name must match file name.
    public static readonly DiagnosticDescriptor Ush0045 = Create(
        "USH0045",
        "Class name must match file name",
        "The class '{0}' must match the file name '{1}'.",
        "Structure",
        DiagnosticSeverity.Warning);
}
