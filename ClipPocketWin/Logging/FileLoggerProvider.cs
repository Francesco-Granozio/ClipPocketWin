using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClipPocketWin.Logging;

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public FileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;
        string? directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        ThrowIfDisposed();
        return _loggers.GetOrAdd(categoryName, category => new FileLogger(category, WriteLine));
    }

    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }

    private void WriteLine(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        string eventToken = eventId.Id != 0 ? $" [EventId:{eventId.Id}]" : string.Empty;

        StringBuilder lineBuilder = new();
        lineBuilder.Append(timestamp)
            .Append(' ')
            .Append('[')
            .Append(logLevel)
            .Append(']')
            .Append(' ')
            .Append(categoryName)
            .Append(eventToken)
            .Append(" - ")
            .Append(message);

        if (exception is not null)
        {
            lineBuilder.AppendLine();
            lineBuilder.Append(exception);
        }

        string line = lineBuilder.ToString();

        lock (_syncRoot)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileLoggerProvider));
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly Action<string, LogLevel, EventId, string, Exception?> _writeAction;

        public FileLogger(string categoryName, Action<string, LogLevel, EventId, string, Exception?> writeAction)
        {
            _categoryName = categoryName;
            _writeAction = writeAction;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            _writeAction(_categoryName, logLevel, eventId, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
