using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

public class JsonFileWriter : ILogWriter
{
    private readonly string _logDirectory;
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

    public JsonFileWriter()
    {
        _logDirectory = ProjectSettings.GlobalizePath("user://logs/godotadvancedlogger");
    }

    public void Initialize()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
            
            string filename = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jsonl";
            _currentFilePath = Path.Combine(_logDirectory, filename);

            var fileStream = new FileStream(_currentFilePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };

            _cts = new CancellationTokenSource();
            _loggingTask = Task.Run(ProcessQueue, _cts.Token);
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

    private async Task ProcessQueue()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            bool wroteAny = false;
            while (_logQueue.TryDequeue(out core.LogEntry entry))
            {
                await WriteLineAsync(entry);
                wroteAny = true;
            }

            if (wroteAny && _writer != null)
                await _writer.FlushAsync();

            await Task.Delay(50);
        }
    }

    private async Task WriteLineAsync(core.LogEntry entry)
    {
        if (_writer == null) return;
        
        var logData = new
        {
            SessionId = core.LogManager.SessionId,
            Timestamp = entry.Timestamp.ToString("O"),
            Level = entry.Level.ToString(),
            Channel = entry.Channel,
            Message = entry.Message,
            Context = entry.ContextData,
            Exception = entry.Exception?.ToString()
        };

        try
        {
            string jsonLine = JsonSerializer.Serialize(logData, JsonOptions);
            await _writer.WriteLineAsync(jsonLine);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[JsonFileWriter] Fehler beim Serialisieren: {e.Message}");
        }
    }

    public void Shutdown()
    {
        _cts?.Cancel();
        try { _loggingTask?.Wait(500); } catch { }
        
        while (_logQueue.TryDequeue(out core.LogEntry entry))
        {
            WriteLineAsync(entry).Wait();
        }

        _writer?.Dispose();
        _cts?.Dispose();
    }
}