using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using UdonSharpLsp.Server.Configuration;
using UdonSharpLsp.Server.PolicyPacks;
using UdonSharpLsp.Server.Workspace;

namespace UdonSharpLsp.Server.Diagnostics;

public sealed class AnalysisService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly AnalyzerRegistry _analyzerRegistry;
    private readonly PolicyRepository _policyRepository;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(
        WorkspaceManager workspaceManager,
        AnalyzerRegistry analyzerRegistry,
        PolicyRepository policyRepository,
        ILogger<AnalysisService> logger)
    {
        _workspaceManager = workspaceManager;
        _analyzerRegistry = analyzerRegistry;
        _policyRepository = policyRepository;
        _logger = logger;
    }

    public async Task<ImmutableArray<Diagnostic>> AnalyzeDocumentAsync(Document document, LinterSettings settings, CancellationToken cancellationToken)
    {
        var project = document.Project;
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var diagnosticOptions = BuildDiagnosticOptions(settings);
        csharpCompilation = csharpCompilation.WithOptions(csharpCompilation.Options.WithSpecificDiagnosticOptions(diagnosticOptions));

        var analyzers = _analyzerRegistry.GetAnalyzers();
        var compilationWithAnalyzers = csharpCompilation.WithAnalyzers(analyzers, options: null, cancellationToken: cancellationToken);

        try
        {
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
            return diagnostics
                .Where(diagnostic => diagnostic.Location == Location.None || diagnostic.Location.GetLineSpan().Path == document.FilePath)
                .ToImmutableArray();
        }
        catch (OperationCanceledException)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analyzer execution failed for {Document}", document.FilePath);
            return ImmutableArray<Diagnostic>.Empty;
        }
    }

    private ImmutableDictionary<string, ReportDiagnostic> BuildDiagnosticOptions(LinterSettings settings)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, ReportDiagnostic>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in _policyRepository.Rules)
        {
            var severity = _policyRepository.GetSeverity(rule.Id, settings);
            builder[rule.Id] = SeverityMapper.ToReportDiagnostic(severity);
        }
        return builder.ToImmutable();
    }
}
