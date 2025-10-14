using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using UdonSharpLsp.Server.Configuration;

namespace UdonSharpLsp.Server.PolicyPacks;

public sealed class PolicyRepository
{
    private ImmutableDictionary<string, PolicyRuleDefinition> _rules = ImmutableDictionary<string, PolicyRuleDefinition>.Empty;

    public ImmutableArray<PolicyRuleDefinition> Rules => _rules.Values.ToImmutableArray();

    public void Replace(ImmutableDictionary<string, PolicyRuleDefinition> rules)
    {
        _rules = rules;
    }

    public PolicyRuleDefinition? GetRule(string id)
    {
        return _rules.TryGetValue(id, out var rule) ? rule : null;
    }

    public DiagnosticSeverity GetSeverity(string id, LinterSettings settings)
    {
        if (_rules.TryGetValue(id, out var rule))
        {
            return rule.GetSeverity(settings.Profile, settings.RuleOverrides);
        }

        return DiagnosticSeverity.Warning;
    }
}

