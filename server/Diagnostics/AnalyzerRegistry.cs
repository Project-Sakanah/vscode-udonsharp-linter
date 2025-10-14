using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using UdonSharpLsp.Server.Diagnostics.Analyzers;

namespace UdonSharpLsp.Server.Diagnostics;

public sealed class AnalyzerRegistry
{
    private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;

    public AnalyzerRegistry()
    {
        _analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new UsnLanguageConstraintsAnalyzer(),
            new UsnTypeConstraintsAnalyzer(),
            new UsnForbiddenNamespaceAnalyzer(),
            new UsnUnityApiAnalyzer(),
            new UsnEventAnalyzer()
        );
    }

    public ImmutableArray<DiagnosticAnalyzer> GetAnalyzers() => _analyzers;
}
