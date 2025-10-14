using System.IO;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using OmniSharp.Extensions.LanguageServer.Server.Logging;
using UdonSharpLsp.Server.Configuration;
using UdonSharpLsp.Server.Diagnostics;
using UdonSharpLsp.Server.Handlers;
using UdonSharpLsp.Server.Infrastructure;
using UdonSharpLsp.Server.PolicyPacks;
using UdonSharpLsp.Server.Services;
using UdonSharpLsp.Server.Workspace;

var baseDirectory = AppContext.BaseDirectory;
var logsDirectory = Path.Combine(baseDirectory, "logs");
Directory.CreateDirectory(logsDirectory);
var serverLogPath = Path.Combine(logsDirectory, "server.log");
var fatalLogPath = Path.Combine(logsDirectory, "fatal.log");

Console.SetOut(TextWriter.Null);

try
{
    var server = await LanguageServer.From(options =>
    {
        options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddLanguageProtocolLogging();
                logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
                logging.AddProvider(new FileLoggerProvider(serverLogPath));
            })
            .WithServices(services =>
            {
                services.AddMediatR(typeof(Program));
                services.AddSingleton(new SettingsProvider());
                services.AddSingleton<PolicyRepository>();
                services.AddSingleton<PolicyPackLoader>();
                services.AddSingleton<MetadataReferenceService>();
                services.AddSingleton<WorkspaceManager>();
                services.AddSingleton<AnalyzerRegistry>();
                services.AddSingleton<AnalysisService>();
                services.AddSingleton<DiagnosticsPublisher>();
                services.AddSingleton(provider => new LinterConfigurationService(
                    provider.GetRequiredService<SettingsProvider>(),
                    provider.GetRequiredService<PolicyPackLoader>(),
                    provider.GetRequiredService<PolicyRepository>(),
                    provider.GetRequiredService<WorkspaceManager>(),
                    provider.GetRequiredService<ILogger<LinterConfigurationService>>(),
                    baseDirectory));
            })
            .WithHandler<UdonSharpTextDocumentSyncHandler>()
            .WithHandler<ConfigurationChangeHandler>()
            .WithHandler<RuleListHandler>()
            .WithHandler<RuleDocumentationHandler>()
            .WithHandler<ServerStatusHandler>()
            .OnInitialize(async (languageServer, request, cancellationToken) =>
            {
                var logger = languageServer.Services.GetRequiredService<ILogger<Program>>();

                try
                {
                    if (request.InitializationOptions is JsonElement element && element.ValueKind == JsonValueKind.Object)
                    {
                        languageServer.Services.GetRequiredService<SettingsProvider>().Update(element);
                    }

                    var settings = languageServer.Services.GetRequiredService<SettingsProvider>().Current;
                    var configurator = languageServer.Services.GetRequiredService<LinterConfigurationService>();
                    await configurator.ApplyAsync(settings, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogCritical(exception, "UdonSharp LSP server failed during initialization.");
                    throw;
                }
            });
    });

    await server.WaitForExit;
}
catch (Exception exception)
{
    File.AppendAllText(fatalLogPath, $"{DateTime.UtcNow:O} FATAL {exception}{Environment.NewLine}");
    throw;
}
