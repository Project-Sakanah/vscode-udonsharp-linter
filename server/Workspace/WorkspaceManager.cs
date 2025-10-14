using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using UdonSharpLsp.Server.Configuration;

namespace UdonSharpLsp.Server.Workspace;

public sealed class WorkspaceManager
{
    private readonly AdhocWorkspace _workspace;
    private readonly MetadataReferenceService _metadataReferenceService;
    private readonly ILogger<WorkspaceManager> _logger;
    private ProjectId? _projectId;
    private readonly ConcurrentDictionary<Uri, DocumentId> _documents = new();

    public WorkspaceManager(MetadataReferenceService metadataReferenceService, ILogger<WorkspaceManager> logger)
    {
        _metadataReferenceService = metadataReferenceService;
        _logger = logger;
        _workspace = new AdhocWorkspace();
    }

    public async Task InitializeAsync(LinterSettings settings, string baseDirectory, CancellationToken cancellationToken)
    {
        var references = _metadataReferenceService.ResolveReferences(settings, baseDirectory);
        var existingDocuments = new List<(Uri uri, string text)>();
        foreach (var (uri, documentId) in _documents)
        {
            var document = _workspace.CurrentSolution.GetDocument(documentId);
            if (document is null)
            {
                continue;
            }

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            existingDocuments.Add((uri, text.ToString()));
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, kind: SourceCodeKind.Regular);
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithAllowUnsafe(false)
            .WithDeterministic(false)
            .WithOptimizationLevel(OptimizationLevel.Debug)
            .WithOverflowChecks(true);

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId("UdonSharpLinter"),
            VersionStamp.Default,
            name: "UdonSharpLinter",
            assemblyName: "UdonSharpLinter",
            language: LanguageNames.CSharp,
            compilationOptions: compilationOptions,
            parseOptions: parseOptions,
            metadataReferences: references
        );

        _workspace.ClearSolution();
        _documents.Clear();
        _workspace.AddProject(projectInfo);
        _projectId = projectInfo.Id;

        foreach (var (uri, text) in existingDocuments)
        {
            await OpenOrUpdateDocumentAsync(uri, text, cancellationToken).ConfigureAwait(false);
        }

        await Task.CompletedTask;
    }

    public Project? CurrentProject => _projectId is { } id ? _workspace.CurrentSolution.GetProject(id) : null;

    public ValueTask<Document?> GetDocumentAsync(Uri documentUri, CancellationToken cancellationToken)
    {
        return new ValueTask<Document?>(_documents.TryGetValue(documentUri, out var documentId)
            ? _workspace.CurrentSolution.GetDocument(documentId)
            : null);
    }

    public async Task<Document?> OpenOrUpdateDocumentAsync(Uri documentUri, string text, CancellationToken cancellationToken)
    {
        if (_projectId is null)
        {
            throw new InvalidOperationException("Workspace not initialized.");
        }

        if (_documents.TryGetValue(documentUri, out var documentId))
        {
            var sourceText = SourceText.From(text);
            var newSolution = _workspace.CurrentSolution.WithDocumentText(documentId, sourceText, PreservationMode.PreserveIdentity);
            if (_workspace.TryApplyChanges(newSolution))
            {
                return newSolution.GetDocument(documentId);
            }

            return _workspace.CurrentSolution.GetDocument(documentId);
        }
        else
        {
            var projectId = _projectId ?? throw new InvalidOperationException("Workspace not initialized.");
            var loader = TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create()));
            var documentInfo = DocumentInfo.Create(
                DocumentId.CreateNewId(projectId, debugName: documentUri.ToString()),
                name: Path.GetFileName(documentUri.LocalPath),
                loader: loader,
                filePath: documentUri.IsFile ? documentUri.LocalPath : documentUri.ToString());

            var newSolution = _workspace.CurrentSolution.AddDocument(documentInfo);
            if (_workspace.TryApplyChanges(newSolution))
            {
                var addedDocument = newSolution.GetDocument(documentInfo.Id);
                _documents[documentUri] = documentInfo.Id;
                return addedDocument;
            }

            return null;
        }
    }

    public void RemoveDocument(Uri documentUri)
    {
        if (_documents.TryRemove(documentUri, out var documentId))
        {
            var newSolution = _workspace.CurrentSolution.RemoveDocument(documentId);
            _workspace.TryApplyChanges(newSolution);
        }
    }
}

