using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Tests.Shared;

/// <summary>Keeps what is logged, so a test can say that a warning was given.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    /// <summary>Everything logged so far.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

    /// <summary>The warnings logged so far.</summary>
    public IReadOnlyList<string> Warnings => [.. _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)];

    public ILogger CreateLogger(string categoryName) => new Logger(_entries);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
