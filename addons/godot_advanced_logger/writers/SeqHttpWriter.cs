using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

public class SeqHttpWriter : ILogWriter
{
    private readonly string _serverUrl;
    private readonly string _apiKey;
    
    private readonly ConcurrentQueue<core.LogEntry> _logQueue = new();
    private CancellationTokenSource _cts;
    private Task _loggingTask;
    private static readonly System.Net.Http.HttpClient HttpClient = new();
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a SeqHttpWriter
    /// </summary>
    /// <param name="serverUrl">Base URL of the Seq-Server</param>
    /// <param name="apiKey">Optional: API Key used for authentication</param>
    public SeqHttpWriter(string serverUrl = "http://localhost:5341", string apiKey = null)
    {
        _serverUrl = serverUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public void Initialize()
    {
        _cts = new CancellationTokenSource();
        _loggingTask = Task.Run(ProcessQueue, _cts.Token);
    }

    public void Write(in core.LogEntry entry)
    {
        _logQueue.Enqueue(entry);
    }

    private async Task ProcessQueue()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var batch = new List<core.LogEntry>();
            
            while (batch.Count < 50 && _logQueue.TryDequeue(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count > 0)
            {
                await SendBatchAsync(batch);
            }
            else
            {
                try { await Task.Delay(200, _cts.Token); } catch (TaskCanceledException) { break; }
            }
        }
    }

    private async Task SendBatchAsync(List<core.LogEntry> batch)
    {
        try
        {
            var payload = new
            {
                Events = new List<object>()
            };

            foreach (var entry in batch)
            {
                string seqLevel = entry.Level switch
                {
                    core.LogLevel.Info => "Information",
                    core.LogLevel.Warning => "Warning",
                    core.LogLevel.Error => "Error",
                    core.LogLevel.Debug => "Debug",
                    _ => "Information"
                };
                
                string normalizedPath = string.IsNullOrEmpty(entry.CallerFilePath) ? "" : entry.CallerFilePath.Replace("\\", "/");
                
                var properties = new Dictionary<string, object>
                {
                    { "SessionId", core.LogManager.SessionId },
                    { "GameVersion", ProjectSettings.GetSetting("application/config/version") },
                    { "Channel", entry.Channel },
                    
                    // Lesbarer String für die Seq-Tabelle
                    { "Caller", $"{Path.GetFileName(entry.CallerFilePath)}:{entry.CallerLineNumber} ({entry.CallerMemberName})" },
                };
                
                if (entry.ContextData != null)
                {
                    foreach (var kvp in entry.ContextData)
                    {
                        if (!properties.ContainsKey(kvp.Key)) 
                        {
                            properties[kvp.Key] = kvp.Value;
                        }
                    }
                }
                
                var eventData = new Dictionary<string, object>
                {
                    { "Timestamp", entry.Timestamp.ToString("O") },
                    { "Level", seqLevel },
                    { "MessageTemplate", entry.Message },
                    { "Properties", properties }
                };

                if (entry.Exception != null)
                {
                    eventData.Add("Exception", entry.Exception.ToString());
                }

                payload.Events.Add(eventData);
            }

            string json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(_apiKey))
            {
                content.Headers.Add("X-Seq-ApiKey", _apiKey);
            }
            
            var response = await HttpClient.PostAsync($"{_serverUrl}/api/events/raw", content, _cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string errorResponse = await response.Content.ReadAsStringAsync();
                GD.PrintErr($"[SeqHttpWriter] HTTP Error {response.StatusCode}: {errorResponse}");
            }
        }
        catch (TaskCanceledException) { /* Ignore on shutdown */ }
        catch (Exception e)
        {
            GD.PrintErr($"[SeqHttpWriter] Network Error: {e.Message}");
        }
    }

    public void Shutdown()
    {
        _cts?.Cancel();
        
        try { _loggingTask?.Wait(1000); } catch { }
        
        var finalBatch = new List<core.LogEntry>();
        while (_logQueue.TryDequeue(out var entry))
        {
            finalBatch.Add(entry);
        }

        if (finalBatch.Count > 0)
        {
            SendBatchAsync(finalBatch).Wait(1000); 
        }

        _cts?.Dispose();
    }
}