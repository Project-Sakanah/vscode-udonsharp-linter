using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using UdonSharpLsp.Server.Diagnostics.Analyzers;

namespace UdonSharpLsp.Server.Diagnostics;

public sealed class AnalyzerRegistry
{
    private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
        new UshNetworkEventAnalyzer(),
        new UshSynchronizationAnalyzer(),
        new UshApiExposureAnalyzer(),
        new UshRuntimeRestrictionAnalyzer(),
        new UshLanguageConstraintsAnalyzer(),
        new UshFieldChangeCallbackAnalyzer(),
        new UshStructureAnalyzer());

    public ImmutableArray<DiagnosticAnalyzer> GetAnalyzers() => _analyzers;
}
