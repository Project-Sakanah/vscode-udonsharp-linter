using System.Collections.Generic;

namespace UdonSharpLsp.Server.Configuration;

public sealed record LinterSettings(
    string Profile,
    IReadOnlyDictionary<string, string> RuleOverrides,
    string UnityApiSurface,
    string? CustomStubPath,
    bool AllowRefOut,
    bool CodeActionsEnabled,
    string Telemetry,
    IReadOnlyList<string> PolicyPackPaths
)
{
    public static LinterSettings Default { get; } = new(
        Profile: "latest",
        RuleOverrides: new Dictionary<string, string>(),
        UnityApiSurface: "bundled-stubs",
        CustomStubPath: null,
        AllowRefOut: false,
        CodeActionsEnabled: true,
        Telemetry: "minimal",
        PolicyPackPaths: Array.Empty<string>()
    );
}
