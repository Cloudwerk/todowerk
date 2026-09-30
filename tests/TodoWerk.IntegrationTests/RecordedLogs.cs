using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// Every log record a host wrote, kept so a test can assert on one. Hand-written rather than taken
/// from a package: the whole of what is needed is a category, a level and a formatted message, and
/// the alternative was a new dependency for three properties.
/// </summary>
public sealed class RecordedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLog> _records = new();

    public IReadOnlyList<RecordedLog> Records => [.. _records];

    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _records);

    public void Dispose()
    {
    }

    private sealed class Recorder(string category, ConcurrentQueue<RecordedLog> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            records.Enqueue(new RecordedLog(category, logLevel, formatter(state, exception)));
    }
}

public sealed record RecordedLog(string Category, LogLevel Level, string Message);
