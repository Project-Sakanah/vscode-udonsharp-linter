using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UdonSharpLsp.Server.PolicyPacks;

public sealed class PolicyPackLoader
{
    private readonly ILogger<PolicyPackLoader> _logger;

    public PolicyPackLoader(ILogger<PolicyPackLoader> logger)
    {
        _logger = logger;
    }

    public ImmutableDictionary<string, PolicyRuleDefinition> Load(string baseDirectory, IEnumerable<string> additionalPacks)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, PolicyRuleDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumeratePolicyFiles(baseDirectory, additionalPacks))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("rules", out var rulesElement))
                {
                    continue;
                }

                foreach (var ruleElement in rulesElement.EnumerateArray())
                {
                    if (TryParseRule(ruleElement, out var rule))
                    {
                        builder[rule.Id] = rule;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load policy pack {File}", file);
            }
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<string> EnumeratePolicyFiles(string baseDirectory, IEnumerable<string> additionalPacks)
    {
        if (Directory.Exists(baseDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(baseDirectory, "*.json", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        foreach (var additional in additionalPacks)
        {
            if (File.Exists(additional))
            {
                yield return additional;
            }
        }
    }

    private static bool TryParseRule(JsonElement element, out PolicyRuleDefinition rule)
    {
        rule = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 } id)
        {
            return false;
        }

        var title = element.GetProperty("title").GetString() ?? id;
        var message = element.GetProperty("message").GetString() ?? string.Empty;
        var category = element.GetProperty("category").GetString() ?? "General";
        var severity = element.GetProperty("defaultSeverity").GetString() ?? "warn";
        var helpUri = element.TryGetProperty("helpUri", out var helpUriElement) ? helpUriElement.GetString() : null;
        var hasCodeFix = element.TryGetProperty("hasCodeFix", out var hasCodeFixElement) && hasCodeFixElement.GetBoolean();

        IDictionary<string, string>? profileSeverities = null;
        if (element.TryGetProperty("profiles", out var profileElement) && profileElement.ValueKind == JsonValueKind.Object)
        {
            profileSeverities = profileElement.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        IDictionary<string, IDictionary<string, string>>? documentation = null;
        if (element.TryGetProperty("documentation", out var documentationElement) && documentationElement.ValueKind == JsonValueKind.Object)
        {
            documentation = new Dictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var localeEntry in documentationElement.EnumerateObject())
            {
                if (localeEntry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var localeDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var docField in localeEntry.Value.EnumerateObject())
                {
                    if (docField.Value.ValueKind == JsonValueKind.String)
                    {
                        localeDict[docField.Name] = docField.Value.GetString() ?? string.Empty;
                    }
                }

                documentation[localeEntry.Name] = localeDict;
            }
        }

        rule = new PolicyRuleDefinition
        {
            Id = id,
            Title = title,
            Message = message,
            Category = category,
            DefaultSeverity = severity,
            HelpUri = helpUri,
            HasCodeFix = hasCodeFix,
            ProfileSeverities = profileSeverities,
            Documentation = documentation,
        };
        return true;
    }
}
