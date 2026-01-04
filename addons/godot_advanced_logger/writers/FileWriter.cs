using Godot;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;

public class FileWriter : ILogWriter
    {
        private readonly string _logDirectory;
        private string _currentFilePath;
        private StreamWriter _writer;
        
        // Thread-safe queue to hold logs waiting to be written
        private readonly ConcurrentQueue<LogEntry> _logQueue = new();
        
        // Task control
        private CancellationTokenSource _cts;
        private Task _loggingTask;

        public FileWriter()
        {
            // user:// so it works on exported games (Windows, Linux, etc.)
            _logDirectory = ProjectSettings.GlobalizePath("user://logs/godotadvancedlogger");
        }

        public void Initialize()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);

                // Create a unique filename: log_yyyy-MM-dd_hh-mm-ss.txt
                string filename = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                _currentFilePath = Path.Combine(_logDirectory, filename);

                // Open the file stream
                var fileStream = new FileStream(_currentFilePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read);
                _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };

                // Start the background listener
                _cts = new CancellationTokenSource();
                _loggingTask = Task.Run(ProcessQueue, _cts.Token);
                
                // Clean up old logs (Keep last 50)
                CleanupOldLogs();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[FileWriter] Failed to init: {e.Message}");
            }
        }

        public void Write(LogEntry entry)
        {
            _logQueue.Enqueue(entry);
        }

        private async Task ProcessQueue()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                bool wroteAny = false;
                
                // Drain the queue
                while (_logQueue.TryDequeue(out LogEntry entry))
                {
                    await WriteToFileAsync(entry);
                    wroteAny = true;
                }

                // If data has been written, flush to disk
                if (wroteAny)
                {
                    await _writer.FlushAsync();
                }

                // Wait a tiny bit to not hog the CPU if queue is empty
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
             
             if (entry.Exception != null)
             {
                 sb.AppendLine().Append(entry.Exception.ToString());
             }

             await _writer.WriteLineAsync(sb.ToString());
        }

        private void CleanupOldLogs()
        {
            // Simple rotation: keep top 50 newest files
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
            
            // Wait for the task to finish writing remaining logs
            try { _loggingTask?.Wait(1000); } catch { /* Ignore task cancellation errors */ }

            // Flush remaining items manually
            while (_logQueue.TryDequeue(out LogEntry entry))
            {
                // Synchronous write for final shutdown
                _writer?.WriteLine($"[SHUTDOWN FLUSH] {entry.Message}");
            }

            _writer?.Dispose();
            _cts?.Dispose();
        }
    }
