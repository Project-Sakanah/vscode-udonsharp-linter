using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace UdonSharpLsp.Server.PolicyPacks;

public static class SeverityMapper
{
    public static ReportDiagnostic ToReportDiagnostic(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Error => ReportDiagnostic.Error,
            DiagnosticSeverity.Warning => ReportDiagnostic.Warn,
            DiagnosticSeverity.Info => ReportDiagnostic.Info,
            DiagnosticSeverity.Hidden => ReportDiagnostic.Hidden,
            _ => ReportDiagnostic.Default,
        };
    }
}
