using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using UdonSharpLsp.Server.Configuration;

namespace UdonSharpLsp.Server.Workspace;

public sealed class MetadataReferenceService
{
    private static readonly HashSet<string> CoreAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Private.CoreLib.dll",
        "System.Runtime.dll",
        "System.Collections.dll",
        "System.Console.dll",
        "System.Linq.dll",
        "System.Linq.Expressions.dll",
        "System.ObjectModel.dll",
        "System.Private.Uri.dll",
        "System.Runtime.Extensions.dll",
        "System.Runtime.Numerics.dll",
        "System.Text.RegularExpressions.dll",
        "System.Private.CoreLib.dll",
    };

    private readonly ILogger<MetadataReferenceService> _logger;

    public MetadataReferenceService(ILogger<MetadataReferenceService> logger)
    {
        _logger = logger;
    }

    public ImmutableArray<MetadataReference> ResolveReferences(LinterSettings settings, string baseDirectory)
    {
        var references = new List<MetadataReference>();
        references.AddRange(GetCoreReferences());

        if (string.Equals(settings.UnityApiSurface, "bundled-stubs", StringComparison.OrdinalIgnoreCase))
        {
            var stubDirectory = Path.Combine(baseDirectory, "Stubs", "Generated");
            references.AddRange(LoadStubAssemblies(stubDirectory));
        }
        else if (string.Equals(settings.UnityApiSurface, "custom-stubs", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(settings.CustomStubPath))
        {
            references.AddRange(LoadStubAssemblies(settings.CustomStubPath));
        }

        return references.ToImmutableArray();
    }

    private IEnumerable<MetadataReference> GetCoreReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            yield break;
        }

        foreach (var assemblyPath in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fileName = Path.GetFileName(assemblyPath);
            if (CoreAssemblyNames.Contains(fileName))
            {
                yield return MetadataReference.CreateFromFile(assemblyPath);
            }
        }
    }

    private IEnumerable<MetadataReference> LoadStubAssemblies(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Unity/VRC stub directory not found: {Directory}", directory);
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            MetadataReference? reference = null;
            try
            {
                reference = MetadataReference.CreateFromFile(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load stub assembly {File}", file);
            }

            if (reference is not null)
            {
                yield return reference;
            }
        }
    }
}
