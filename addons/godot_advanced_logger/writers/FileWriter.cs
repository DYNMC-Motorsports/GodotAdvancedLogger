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
/// Appends log entries to a tab-separated <c>.txt</c> file under <c>user://logs/godotadvancedlogger</c>.
/// Writing happens on a background thread so the game loop never blocks on disk I/O.
/// </summary>
public class FileWriter : ILogWriter
{
    private const string FilePattern = "log_*.txt";
    private const int FlushIntervalMs = 50;

    private readonly string _logDirectory;
    private readonly int _maxFiles;

    private string _currentFilePath;
    private StreamWriter _writer;

    private readonly ConcurrentQueue<core.LogEntry> _logQueue = new();

    private CancellationTokenSource _cts;
    private Task _loggingTask;

    /// <param name="maxFiles">How many log files to keep before the oldest are deleted.</param>
    public FileWriter(int maxFiles = 50)
    {
        _logDirectory = ProjectSettings.GlobalizePath(core.LoggerSettings.LogDirectory);
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

            string filename = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            _currentFilePath = Path.Combine(_logDirectory, filename);

            var fileStream = new FileStream(_currentFilePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
            
            _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = false };

            _cts = new CancellationTokenSource();
            _loggingTask = Task.Run(() => ProcessQueue(_cts.Token), _cts.Token);

            LogFileRetention.Apply(_logDirectory, FilePattern, _maxFiles, nameof(FileWriter));
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FileWriter] Failed to init: {e.Message}");
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
            {
                await _writer.FlushAsync();
            }

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
        await _writer.WriteLineAsync(FormatLine(entry));
    }

    /// <summary>
    /// Renders one entry as a tab-separated line. Shared by the background loop and the
    /// shutdown flush so entries written during teardown keep their level, channel,
    /// context and exception instead of degrading to a bare message.
    /// </summary>
    private static string FormatLine(in core.LogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append('\t').Append(entry.Level.ToString().ToUpperInvariant());
        sb.Append('\t').Append(entry.Channel);
        sb.Append('\t').Append(entry.Message);

        if (entry.ContextData is { Count: > 0 })
        {
            try
            {
                sb.Append('\t').Append(JsonSerializer.Serialize(entry.ContextData));
            }
            catch (Exception e)
            {
                sb.Append("\t<context could not be serialized: ").Append(e.Message).Append('>');
            }
        }

        if (entry.Exception != null)
        {
            sb.AppendLine().Append(entry.Exception);
        }

        return sb.ToString();
    }

    public void Shutdown()
    {
        _cts?.Cancel();

        try { _loggingTask?.Wait(500); } catch { /* worker is being torn down */ }

        try
        {
            while (_logQueue.TryDequeue(out core.LogEntry entry))
            {
                _writer?.WriteLine(FormatLine(entry));
            }

            _writer?.Flush();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FileWriter] Final flush failed: {e.Message}");
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
