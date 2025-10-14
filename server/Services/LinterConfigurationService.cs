using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UdonSharpLsp.Server.Configuration;
using UdonSharpLsp.Server.PolicyPacks;
using UdonSharpLsp.Server.Workspace;

namespace UdonSharpLsp.Server.Services;

public sealed class LinterConfigurationService
{
    private readonly SettingsProvider _settingsProvider;
    private readonly PolicyPackLoader _policyPackLoader;
    private readonly PolicyRepository _policyRepository;
    private readonly WorkspaceManager _workspaceManager;
    private readonly ILogger<LinterConfigurationService> _logger;
    private readonly string _baseDirectory;

    public LinterConfigurationService(
        SettingsProvider settingsProvider,
        PolicyPackLoader policyPackLoader,
        PolicyRepository policyRepository,
        WorkspaceManager workspaceManager,
        ILogger<LinterConfigurationService> logger,
        string baseDirectory)
    {
        _settingsProvider = settingsProvider;
        _policyPackLoader = policyPackLoader;
        _policyRepository = policyRepository;
        _workspaceManager = workspaceManager;
        _logger = logger;
        _baseDirectory = baseDirectory;
    }

    public async Task ApplyAsync(LinterSettings settings, CancellationToken cancellationToken)
    {
        _settingsProvider.Update(settings);
        var policyDirectory = Path.Combine(_baseDirectory, "PolicyPacks");
        var rules = _policyPackLoader.Load(policyDirectory, settings.PolicyPackPaths);
        _policyRepository.Replace(rules);
        await _workspaceManager.InitializeAsync(settings, _baseDirectory, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Applied configuration profile {Profile} with {RuleCount} rules", settings.Profile, rules.Count);
    }
}
