using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace UdonSharpLsp.Server.Services;

public sealed class DiagnosticsPublisher
{
    private readonly ILanguageServerFacade _server;

    public DiagnosticsPublisher(ILanguageServerFacade server)
    {
        _server = server;
    }

    public Task PublishAsync(Uri documentUri, IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var lspDiagnostics = diagnostics.Select(ToLspDiagnostic).ToArray();
        var publishParams = new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(documentUri),
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(lspDiagnostics),
        };
        cancellationToken.ThrowIfCancellationRequested();
        _server.Client.SendNotification(TextDocumentNames.PublishDiagnostics, publishParams);
        return Task.CompletedTask;
    }

    private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic ToLspDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        Position start;
        Position end;
        if (!diagnostic.Location.IsInSource)
        {
            start = end = new Position(0, 0);
        }
        else
        {
            var range = diagnostic.Location.GetLineSpan();
            start = new Position(range.StartLinePosition.Line, range.StartLinePosition.Character);
            end = new Position(range.EndLinePosition.Line, range.EndLinePosition.Character);
        }

        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
        {
            Code = diagnostic.Id,
            Message = diagnostic.GetMessage(),
            Severity = diagnostic.Severity switch
            {
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Info => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
                _ => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
            },
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(start, end),
            Source = "UdonSharp",
        };
    }
}



