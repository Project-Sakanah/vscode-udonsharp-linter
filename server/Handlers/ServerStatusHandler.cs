using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using UdonSharpLsp.Server.Configuration;
using UdonSharpLsp.Server.Contracts;
using UdonSharpLsp.Server.PolicyPacks;

namespace UdonSharpLsp.Server.Handlers;

[Method("udonsharp/server/status")]
public sealed class ServerStatusHandler : IJsonRpcRequestHandler<ServerStatusRequest, ServerStatusResponse>
{
    private readonly PolicyRepository _policyRepository;
    private readonly SettingsProvider _settingsProvider;

    public ServerStatusHandler(PolicyRepository policyRepository, SettingsProvider settingsProvider)
    {
        _policyRepository = policyRepository;
        _settingsProvider = settingsProvider;
    }

    public Task<ServerStatusResponse> Handle(ServerStatusRequest request, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.Current;
        var totalCount = _policyRepository.Rules.Length;
        var disabledCount = _policyRepository.Rules.Count(rule => _policyRepository.GetSeverity(rule.Id, settings) == Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);
        var version = typeof(ServerStatusHandler).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var response = new ServerStatusResponse(settings.Profile, disabledCount, totalCount, version);
        return Task.FromResult(response);
    }
}
