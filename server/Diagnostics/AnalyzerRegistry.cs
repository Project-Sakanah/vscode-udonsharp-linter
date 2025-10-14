using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
namespace UdonSharpLsp.Server.Diagnostics;

public sealed class AnalyzerRegistry
{
    private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers = ImmutableArray<DiagnosticAnalyzer>.Empty;

    public ImmutableArray<DiagnosticAnalyzer> GetAnalyzers() => _analyzers;
}
