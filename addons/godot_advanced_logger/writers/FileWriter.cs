using Godot;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class FileWriter : ILogWriter
{
    private readonly string _logDirectory;
    private string _currentFilePath;
    private StreamWriter _writer;
    
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    
    private CancellationTokenSource _cts;
    private Task _loggingTask;

    public FileWriter()
    {
        _logDirectory = ProjectSettings.GlobalizePath("user://logs/godotadvancedlogger");
    }

    public void Initialize()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            string filename = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            _currentFilePath = Path.Combine(_logDirectory, filename);

            var fileStream = new FileStream(_currentFilePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };

            _cts = new CancellationTokenSource();
            _loggingTask = Task.Run(ProcessQueue, _cts.Token);
            
            CleanupOldLogs();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FileWriter] Failed to init: {e.Message}");
        }
    }

    public void Write(in LogEntry entry)
    {
        // Da ConcurrentQueue structs By-Value nimmt, wird hier eine Kopie auf den Heap/Queue gelegt.
        // Das ist für das asynchrone Schreiben notwendig und völlig in Ordnung.
        _logQueue.Enqueue(entry);
    }

    private async Task ProcessQueue()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            bool wroteAny = false;
            
            while (_logQueue.TryDequeue(out LogEntry entry))
            {
                await WriteToFileAsync(entry);
                wroteAny = true;
            }

            if (wroteAny && _writer != null)
            {
                await _writer.FlushAsync();
            }

            await Task.Delay(50);
        }
    }

    private async Task WriteToFileAsync(LogEntry entry)
    {
         if (_writer == null) return;

         var sb = new StringBuilder();
         sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
         sb.Append('\t').Append(entry.Level.ToString().ToUpper());
         sb.Append('\t').Append(entry.Channel);
         sb.Append('\t').Append(entry.Message);
         
         if (entry.ContextData != null && entry.ContextData.Count > 0)
         {
             try 
             {
                 sb.Append('\t').Append(JsonSerializer.Serialize(entry.ContextData));
             } 
             catch { /* Ignore */ }
         }

         if (entry.Exception != null)
         {
             sb.AppendLine().Append(entry.Exception.ToString());
         }

         await _writer.WriteLineAsync(sb.ToString());
    }

    private void CleanupOldLogs()
    {
        try
        {
            var info = new DirectoryInfo(_logDirectory);
            var files = info.GetFiles("log_*.txt")
                            .OrderByDescending(f => f.CreationTime)
                            .ToList();

            if (files.Count > 50)
            {
                for (int i = 50; i < files.Count; i++)
                {
                    files[i].Delete();
                }
            }
        }
        catch (Exception e) { GD.PrintErr($"[FileWriter] Cleanup failed: {e.Message}"); }
    }

    public void Shutdown()
    {
        _cts?.Cancel();
        
        try { _loggingTask?.Wait(1000); } catch { }

        while (_logQueue.TryDequeue(out LogEntry entry))
        {
            _writer?.WriteLine($"[SHUTDOWN FLUSH] {entry.Message}");
        }

        _writer?.Dispose();
        _cts?.Dispose();
    }
}