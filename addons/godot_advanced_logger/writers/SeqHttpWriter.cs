using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Pitwall.addons.godot_advanced_logger.writers;

public class SeqHttpWriter : ILogWriter
{
    private readonly string _serverUrl;
    private readonly string _apiKey;
    
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private CancellationTokenSource _cts;
    private Task _loggingTask;
    private static readonly System.Net.Http.HttpClient _httpClient = new();
    
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        //PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initialisiert den Seq-Writer.
    /// </summary>
    /// <param name="serverUrl">Die Basis-URL des Seq-Servers</param>
    /// <param name="apiKey">Optional: Ein API-Key, falls in Seq konfiguriert</param>
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

    public void Write(in LogEntry entry)
    {
        _logQueue.Enqueue(entry);
    }

    private async Task ProcessQueue()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var batch = new List<LogEntry>();
            
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

    private async Task SendBatchAsync(List<LogEntry> batch)
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
                    LogLevel.Info => "Information",
                    LogLevel.Warning => "Warning",
                    LogLevel.Error => "Error",
                    LogLevel.Debug => "Debug",
                    _ => "Information"
                };
                
                var properties = new Dictionary<string, object>
                {
                    { "SessionId", LogManager.SessionId },
                    { "GameVersion", ProjectSettings.GetSetting("application/config/version") },
                    { "Channel", entry.Channel }
                };

                if (entry.ContextData != null)
                {
                    foreach (var kvp in entry.ContextData)
                    {
                        // Überschreibe "Channel" nicht aus Versehen
                        if (kvp.Key != "Channel") properties[kvp.Key] = kvp.Value;
                    }
                }

                payload.Events.Add(new
                {
                    Timestamp = entry.Timestamp.ToString("O"), // ISO 8601 Format
                    Level = seqLevel,
                    MessageTemplate = entry.Message, // Seq nutzt MessageTemplate
                    Properties = properties,
                    Exception = entry.Exception?.ToString()
                });
            }

            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(_apiKey))
            {
                content.Headers.Add("X-Seq-ApiKey", _apiKey);
            }

            // Der Endpunkt für raw JSON in Seq
            var response = await _httpClient.PostAsync($"{_serverUrl}/api/events/raw", content, _cts.Token);

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

        // Restliche Logs flushen
        var finalBatch = new List<LogEntry>();
        while (_logQueue.TryDequeue(out var entry))
        {
            finalBatch.Add(entry);
        }

        if (finalBatch.Count > 0)
        {
            // Synchron warten beim Herunterfahren
            SendBatchAsync(finalBatch).Wait(1000); 
        }

        _cts?.Dispose();
    }
}