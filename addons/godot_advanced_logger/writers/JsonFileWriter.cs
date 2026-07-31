using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

/// <summary>
/// Appends log entries as JSON Lines (<c>.jsonl</c>) under <c>user://logs/godotadvancedlogger</c>,
/// one self-contained JSON object per line, for programmatic parsing.
/// </summary>
public class JsonFileWriter : ILogWriter
{
    private const string FilePattern = "log_*.jsonl";
    private const int FlushIntervalMs = 50;

    private readonly string _logDirectory;
    private readonly int _maxFiles;

    private string _currentFilePath;
    private StreamWriter _writer;

    private readonly ConcurrentQueue<core.LogEntry> _logQueue = new();
    private CancellationTokenSource _cts;
    private Task _loggingTask;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="maxFiles">How many log files to keep before the oldest are deleted.</param>
    public JsonFileWriter(int maxFiles = 50)
    {
        _logDirectory = ProjectSettings.GlobalizePath("user://logs/godotadvancedlogger");
        _maxFiles = maxFiles;
    }

    /// <summary>Absolute path of the file this writer is currently appending to.</summary>
    public string CurrentFilePath => _currentFilePath;

    public void Initialize()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            string filename = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jsonl";
            _currentFilePath = Path.Combine(_logDirectory, filename);

            var fileStream = new FileStream(_currentFilePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
            
            _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = false };

            _cts = new CancellationTokenSource();
            _loggingTask = Task.Run(() => ProcessQueue(_cts.Token), _cts.Token);
            
            LogFileRetention.Apply(_logDirectory, FilePattern, _maxFiles, nameof(JsonFileWriter));
        }
        catch (Exception e)
        {
            GD.PrintErr($"[JsonFileWriter] Error while initializing: {e.Message}");
        }
    }

    public void Write(in core.LogEntry entry)
    {
        _logQueue.Enqueue(entry);
    }

    private async Task ProcessQueue(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool wroteAny = false;

            while (_logQueue.TryDequeue(out core.LogEntry entry))
            {
                await WriteLineAsync(entry);
                wroteAny = true;
            }

            if (wroteAny && _writer != null)
                await _writer.FlushAsync();

            try
            {
                await Task.Delay(FlushIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task WriteLineAsync(core.LogEntry entry)
    {
        if (_writer == null) return;

        string jsonLine = FormatLine(entry);
        if (jsonLine == null) return;

        await _writer.WriteLineAsync(jsonLine);
    }

    /// <summary>Serializes one entry to a single JSON line, or <c>null</c> if it cannot be serialized.</summary>
    private static string FormatLine(in core.LogEntry entry)
    {
        var logData = new
        {
            SessionId = core.LogManager.SessionId,
            Timestamp = entry.Timestamp.ToString("O"),
            Level = entry.Level.ToString(),
            Channel = entry.Channel,
            Caller = $"{Path.GetFileName(entry.CallerFilePath)}:{entry.CallerLineNumber} ({entry.CallerMemberName})",
            Message = entry.Message,
            Context = entry.ContextData,
            Exception = entry.Exception?.ToString()
        };

        try
        {
            return JsonSerializer.Serialize(logData, JsonOptions);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[JsonFileWriter] Could not serialize entry '{entry.Message}': {e.Message}");
            return null;
        }
    }

    public void Shutdown()
    {
        _cts?.Cancel();

        try { _loggingTask?.Wait(500); } catch { /* worker is being torn down */ }

        try
        {
            while (_logQueue.TryDequeue(out core.LogEntry entry))
            {
                string line = FormatLine(entry);
                if (line != null) _writer?.WriteLine(line);
            }

            _writer?.Flush();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[JsonFileWriter] Final flush failed: {e.Message}");
        }
        finally
        {
            _writer?.Dispose();
            _writer = null;

            _cts?.Dispose();
            _cts = null;
        }
    }
}
