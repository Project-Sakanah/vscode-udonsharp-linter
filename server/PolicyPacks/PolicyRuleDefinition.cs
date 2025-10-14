using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace UdonSharpLsp.Server.PolicyPacks;

public sealed class PolicyRuleDefinition
{
    public string? GetDocumentation(string locale, string fallbackLocale = "en-US")
    {
        if (Documentation is null)
        {
            return null;
        }

        if (Documentation.TryGetValue(locale, out var localized) && localized.TryGetValue("markdown", out var markdown))
        {
            return markdown;
        }

        if (Documentation.TryGetValue(fallbackLocale, out var fallback) && fallback.TryGetValue("markdown", out var fallbackMarkdown))
        {
            return fallbackMarkdown;
        }

        return null;
    }

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string Category { get; init; }
    public required string DefaultSeverity { get; init; }
    public string? HelpUri { get; init; }
    public bool HasCodeFix { get; init; }
    public IDictionary<string, string>? ProfileSeverities { get; init; }
    public IDictionary<string, IDictionary<string, string>>? Documentation { get; init; }

    public DiagnosticSeverity GetSeverity(string profile, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.TryGetValue(Id, out var overrideSeverity))
        {
            return ToSeverity(overrideSeverity);
        }

        if (ProfileSeverities != null && ProfileSeverities.TryGetValue(profile, out var profileSeverity))
        {
            return ToSeverity(profileSeverity);
        }

        return ToSeverity(DefaultSeverity);
    }

    private static DiagnosticSeverity ToSeverity(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warn" or "warning" => DiagnosticSeverity.Warning,
            "info" or "information" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            "off" => DiagnosticSeverity.Hidden,
            _ => DiagnosticSeverity.Warning,
        };
    }
}
