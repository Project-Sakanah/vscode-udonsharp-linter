using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using UdonSharpLsp.Server.Configuration;
using UdonSharpLsp.Server.Services;
using Newtonsoft.Json.Linq;

namespace UdonSharpLsp.Server.Handlers;

public sealed class ConfigurationChangeHandler : DidChangeConfigurationHandlerBase
{
    private readonly LinterConfigurationService _configurationService;

    public ConfigurationChangeHandler(LinterConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public override async Task<Unit> Handle(DidChangeConfigurationParams request, CancellationToken cancellationToken)
    {
        if (TryExtractSettingsElement(request.Settings, out var linterSettingsElement, out var disposableDocument))
        {
            try
            {
                var settings = Deserialize(linterSettingsElement);
                await _configurationService.ApplyAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                disposableDocument?.Dispose();
            }
        }

        return Unit.Value;
    }

    private static LinterSettings Deserialize(JsonElement element)
    {
        var provider = new SettingsProvider();
        provider.Update(element);
        return provider.Current;
    }

    private static bool TryExtractSettingsElement(object? payload, out JsonElement element, out JsonDocument? document)
    {
        element = default;
        document = null;

        if (payload is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("udonsharpLinter", out var linterElement) && linterElement.ValueKind == JsonValueKind.Object)
            {
                element = linterElement;
                return true;
            }
            return false;
        }

        if (payload is JToken token)
        {
            var linterToken = token["udonsharpLinter"];
            if (linterToken is null)
            {
                return false;
            }

            document = JsonDocument.Parse(linterToken.ToString());
            element = document.RootElement;
            return true;
        }

        return false;
    }
}
