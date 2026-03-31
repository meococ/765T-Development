using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Infrastructure.Logging;

internal sealed class FileLogger : IAgentLogger
{
    private static readonly AsyncLocal<LogScopeState?> ScopeState = new AsyncLocal<LogScopeState?>();
    private readonly object _sync = new object();
    private readonly string _logDirectory = string.Empty;
    private readonly bool _jsonLogFormat;
    private readonly bool _verbose;

    internal FileLogger(bool verbose, bool jsonLogFormat = true, string? logDirectoryOverride = null)
    {
        _verbose = verbose;
        _jsonLogFormat = jsonLogFormat;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDirectory = string.IsNullOrWhiteSpace(logDirectoryOverride)
            ? Path.Combine(appData, BridgeConstants.AppDataFolderName, "logs")
            : logDirectoryOverride!;

        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public IDisposable BeginScope(string? correlationId, string? toolName = null, string? source = null)
    {
        var previous = ScopeState.Value;
        ScopeState.Value = new LogScopeState(
            string.IsNullOrWhiteSpace(correlationId) ? previous?.CorrelationId ?? string.Empty : correlationId!,
            string.IsNullOrWhiteSpace(toolName) ? previous?.ToolName ?? string.Empty : toolName!,
            string.IsNullOrWhiteSpace(source) ? previous?.Source ?? string.Empty : source!);
        return new ScopeHandle(previous);
    }

    private void Write(string level, string message, Exception? ex)
    {
        if (!_verbose && string.Equals(level, "INFO", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = Path.Combine(_logDirectory, DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        var line = _jsonLogFormat ? BuildJsonLine(level, message, ex) : BuildPlainTextLine(level, message, ex);

        try
        {
            lock (_sync)
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException ioEx)
        {
            var fallback = $"FileLogger I/O failure: {ioEx.Message}";
            System.Diagnostics.Trace.WriteLine(fallback);
            try { Console.Error.WriteLine(fallback); } catch { /* absolute last resort */ }
        }
        catch (UnauthorizedAccessException authEx)
        {
            var fallback = $"FileLogger access failure: {authEx.Message}";
            System.Diagnostics.Trace.WriteLine(fallback);
            try { Console.Error.WriteLine(fallback); } catch { /* absolute last resort */ }
        }
    }

    private string BuildJsonLine(string level, string message, Exception? ex)
    {
        var scope = ScopeState.Value;
        var entry = new LogEntry
        {
            TimestampUtc = DateTime.UtcNow.ToString("O"),
            Level = level,
            Message = message,
            CorrelationId = scope?.CorrelationId ?? string.Empty,
            ToolName = scope?.ToolName ?? string.Empty,
            Source = scope?.Source ?? string.Empty,
            Exception = ex?.ToString() ?? string.Empty
        };

        return JsonUtil.Serialize(entry);
    }

    private string BuildPlainTextLine(string level, string message, Exception? ex)
    {
        var scope = ScopeState.Value;
        var builder = new StringBuilder();
        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        builder.Append(" [").Append(level).Append(']');
        if (scope != null)
        {
            if (!string.IsNullOrWhiteSpace(scope.CorrelationId))
            {
                builder.Append(" [corr:").Append(scope.CorrelationId).Append(']');
            }

            if (!string.IsNullOrWhiteSpace(scope.ToolName))
            {
                builder.Append(" [tool:").Append(scope.ToolName).Append(']');
            }

            if (!string.IsNullOrWhiteSpace(scope.Source))
            {
                builder.Append(" [src:").Append(scope.Source).Append(']');
            }
        }

        builder.Append(' ').Append(message);
        if (ex != null)
        {
            builder.AppendLine();
            builder.Append(ex);
        }

        return builder.ToString();
    }

    [DataContract]
    private sealed class LogEntry
    {
        [DataMember(Order = 1)]
        public string TimestampUtc { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string Level { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string Message { get; set; } = string.Empty;

        [DataMember(Order = 4)]
        public string CorrelationId { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public string ToolName { get; set; } = string.Empty;

        [DataMember(Order = 6)]
        public string Source { get; set; } = string.Empty;

        [DataMember(Order = 7)]
        public string Exception { get; set; } = string.Empty;
    }

    private sealed class LogScopeState
    {
        internal LogScopeState(string correlationId, string toolName, string source)
        {
            CorrelationId = correlationId;
            ToolName = toolName;
            Source = source;
        }

        internal string CorrelationId { get; }
        internal string ToolName { get; }
        internal string Source { get; }
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly LogScopeState? _previous;
        private int _disposed;

        internal ScopeHandle(LogScopeState? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ScopeState.Value = _previous;
            }
        }
    }
}
