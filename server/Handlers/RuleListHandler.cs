using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using UdonSharpLsp.Server.Configuration;
using UdonSharpLsp.Server.Contracts;
using UdonSharpLsp.Server.PolicyPacks;

namespace UdonSharpLsp.Server.Handlers;

[Method("udonsharp/rules/list")]
public sealed class RuleListHandler : IJsonRpcRequestHandler<ListRulesRequest, RuleDescriptorDto[]>
{
    private readonly PolicyRepository _policyRepository;
    private readonly SettingsProvider _settingsProvider;

    public RuleListHandler(PolicyRepository policyRepository, SettingsProvider settingsProvider)
    {
        _policyRepository = policyRepository;
        _settingsProvider = settingsProvider;
    }

    public Task<RuleDescriptorDto[]> Handle(ListRulesRequest request, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.Current;
        var result = _policyRepository.Rules
            .Select(rule => new RuleDescriptorDto(
                rule.Id,
                rule.Title,
                rule.Category,
                ToLspSeverity(rule.GetSeverity(settings.Profile, settings.RuleOverrides)),
                rule.Message,
                rule.HelpUri,
                rule.HasCodeFix,
                rule.ProfileSeverities?.ToDictionary(
                    entry => entry.Key,
                    entry => ToLspSeverity(rule.GetSeverity(entry.Key, settings.RuleOverrides)),
                    StringComparer.OrdinalIgnoreCase)
            ))
            .OrderBy(rule => rule.Id)
            .ToArray();

        return Task.FromResult(result);
    }

    private static DiagnosticSeverity ToLspSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity severity)
    {
        return severity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info => DiagnosticSeverity.Information,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden => DiagnosticSeverity.Hint,
            _ => DiagnosticSeverity.Warning,
        };
    }
}
