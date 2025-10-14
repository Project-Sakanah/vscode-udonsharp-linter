using System.Collections.Generic;
using System.Text.Json;

namespace UdonSharpLsp.Server.Configuration;

public sealed class SettingsProvider
{
    private LinterSettings _settings = LinterSettings.Default;

    public LinterSettings Current => _settings;

    public void Update(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            _settings = Deserialize(element) ?? LinterSettings.Default;
        }
    }

    public void Update(LinterSettings settings)
    {
        _settings = settings;
    }

    private static LinterSettings? Deserialize(JsonElement element)
    {
        try
        {
            var profile = element.GetPropertyOrDefault("profile", "latest");
            var unityApiSurface = element.GetPropertyOrDefault("unityApiSurface", "bundled-stubs");
            var customStubPath = element.TryGetProperty("customStubPath", out var customStubElement) && customStubElement.ValueKind == JsonValueKind.String
                ? customStubElement.GetString()
                : null;
            var allowRefOut = element.GetPropertyOrDefault("allowRefOut", false);
            var codeActionsEnabled = element.GetPropertyOrDefault("codeActionsEnabled", true);
            var telemetry = element.GetPropertyOrDefault("telemetry", "minimal");

            var ruleOverrides = element.TryGetProperty("ruleOverrides", out var ruleOverridesElement)
                ? ruleOverridesElement.Deserialize<Dictionary<string, string>>() ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();

            var policyPackPaths = element.TryGetProperty("policyPackPaths", out var policyPackElement)
                ? policyPackElement.Deserialize<List<string>>() ?? new List<string>()
                : new List<string>();

            return new LinterSettings(
                profile,
                ruleOverrides,
                unityApiSurface,
                customStubPath,
                allowRefOut,
                codeActionsEnabled,
                telemetry,
                policyPackPaths
            );
        }
        catch
        {
            return null;
        }
    }
}

internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName, string defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? defaultValue
            : defaultValue;
    }

    public static bool GetPropertyOrDefault(this JsonElement element, string propertyName, bool defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True
            ? true
            : element.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.False
                ? false
                : defaultValue;
    }
}
