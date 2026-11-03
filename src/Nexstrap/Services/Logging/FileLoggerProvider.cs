using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Logging;

/// <summary>
/// Minimal file logger: one file per session, background writer, keeps the newest N files.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int KeepFiles = 10;

    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 4096);
    private readonly Thread _writer;
    private readonly StreamWriter _stream;

    public FileLoggerProvider(string directory)
    {
        Directory.CreateDirectory(directory);
        PruneOldLogs(directory);

        CurrentFile = Path.Combine(directory, $"Nexstrap_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.log");
        _stream = new StreamWriter(new FileStream(CurrentFile, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
        {
            AutoFlush = true
        };

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Nexstrap log writer" };
        _writer.Start();
    }

    public string CurrentFile { get; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Enqueue(string line)
    {
        // Drop rather than block the UI thread if the disk is pathologically slow.
        _queue.TryAdd(line);
    }

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try { _stream.WriteLine(line); }
            catch (IOException) { /* nothing sensible to do */ }
        }
    }

    private static void PruneOldLogs(string directory)
    {
        try
        {
            foreach (var old in new DirectoryInfo(directory).GetFiles("Nexstrap_*.log")
                         .OrderByDescending(f => f.CreationTimeUtc)
                         .Skip(KeepFiles - 1))
            {
                old.Delete();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
        _stream.Dispose();
        _queue.Dispose();
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        private readonly string _category = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ')
                .Append(LevelTag(logLevel)).Append(" [").Append(_category).Append("] ")
                .Append(formatter(state, exception));
            if (exception is not null)
                sb.AppendLine().Append(exception);

            provider.Enqueue(sb.ToString());
        }

        private static string LevelTag(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }
}
