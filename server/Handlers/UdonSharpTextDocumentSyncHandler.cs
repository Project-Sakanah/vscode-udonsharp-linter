using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using UdonSharpLsp.Server.Configuration;
using UdonSharpLsp.Server.Diagnostics;
using UdonSharpLsp.Server.Services;
using UdonSharpLsp.Server.Workspace;

namespace UdonSharpLsp.Server.Handlers;

public sealed class UdonSharpTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly AnalysisService _analysisService;
    private readonly DiagnosticsPublisher _diagnosticsPublisher;
    private readonly SettingsProvider _settingsProvider;
    private readonly ILogger<UdonSharpTextDocumentSyncHandler> _logger;

    public UdonSharpTextDocumentSyncHandler(
        WorkspaceManager workspaceManager,
        AnalysisService analysisService,
        DiagnosticsPublisher diagnosticsPublisher,
        SettingsProvider settingsProvider,
        ILogger<UdonSharpTextDocumentSyncHandler> logger)
    {
        _workspaceManager = workspaceManager;
        _analysisService = analysisService;
        _diagnosticsPublisher = diagnosticsPublisher;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        => new(uri, "csharp");

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(SynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = new DocumentSelector(new DocumentFilter { Language = "csharp" }),
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = true },
        };
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        await _workspaceManager.OpenOrUpdateDocumentAsync(request.TextDocument.Uri.ToUri(), request.TextDocument.Text ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await AnalyzeAndPublishAsync(request.TextDocument.Uri.ToUri(), cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var change = request.ContentChanges?.LastOrDefault();
        var text = change?.Text ?? string.Empty;
        await _workspaceManager.OpenOrUpdateDocumentAsync(request.TextDocument.Uri.ToUri(), text, cancellationToken).ConfigureAwait(false);
        await AnalyzeAndPublishAsync(request.TextDocument.Uri.ToUri(), cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        _workspaceManager.RemoveDocument(request.TextDocument.Uri.ToUri());
        return _diagnosticsPublisher.PublishAsync(request.TextDocument.Uri.ToUri(), Array.Empty<Microsoft.CodeAnalysis.Diagnostic>(), cancellationToken)
            .ContinueWith(_ => Unit.Value, cancellationToken);
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        // Nothing special on save; analysis already happens on change.
        return Task.FromResult(Unit.Value);
    }

    private async Task AnalyzeAndPublishAsync(Uri documentUri, CancellationToken cancellationToken)
    {
        var document = await _workspaceManager.GetDocumentAsync(documentUri, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        var diagnostics = await _analysisService.AnalyzeDocumentAsync(document, _settingsProvider.Current, cancellationToken).ConfigureAwait(false);
        await _diagnosticsPublisher.PublishAsync(documentUri, diagnostics, cancellationToken).ConfigureAwait(false);
    }
}
