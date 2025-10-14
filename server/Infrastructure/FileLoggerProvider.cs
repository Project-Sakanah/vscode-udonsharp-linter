using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace UdonSharpLsp.Server.Infrastructure;

/// <summary>
/// Extremely small file logger used while the language server initialises.
/// Keeps logging away from stdout so LSP framing is not affected.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _path = path;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_path, categoryName, _gate);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _path;
        private readonly string _category;
        private readonly object _gate;

        public FileLogger(string path, string category, object gate)
        {
            _path = path;
            _category = category;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var builder = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("O"))
                .Append(" [")
                .Append(logLevel)
                .Append("] ")
                .Append(_category)
                .Append(": ")
                .Append(formatter(state, exception));

            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            lock (_gate)
            {
                File.AppendAllText(_path, builder.AppendLine().ToString(), Encoding.UTF8);
            }
        }
    }
}
