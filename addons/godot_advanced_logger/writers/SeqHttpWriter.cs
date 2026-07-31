using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

/// <summary>
/// Streams log entries to a Seq server (https://datalust.co/seq) over its raw events API.
/// </summary>
public class SeqHttpWriter : ILogWriter
{
    private const int BatchSize = 50;
    private const int IdlePollMs = 200;
    
    private const int MaxQueuedEntries = 10_000;

    private const int MaxBackoffMs = 30_000;
    
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(3);

    private readonly string _serverUrl;
    private readonly string _apiKey;

    private readonly ConcurrentQueue<core.LogEntry> _logQueue = new();
    private int _queuedCount;
    private long _droppedCount;

    private CancellationTokenSource _cts;
    private Task _loggingTask;

    /// <summary>Cached on the main thread during <see cref="Initialize"/>; ProjectSettings is not thread-safe.</summary>
    private string _gameVersion = string.Empty;

    private int _consecutiveFailures;
    private bool _hasReportedOutage;

    private static readonly System.Net.Http.HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

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
        _serverUrl = (serverUrl ?? "http://localhost:5341").TrimEnd('/');
        _apiKey = apiKey;
    }

    public void Initialize()
    {
        _gameVersion = ProjectSettings.HasSetting("application/config/version")
            ? ProjectSettings.GetSetting("application/config/version").AsString()
            : string.Empty;

        _cts = new CancellationTokenSource();
        _loggingTask = Task.Run(() => ProcessQueue(_cts.Token), _cts.Token);
    }

    public void Write(in core.LogEntry entry)
    {
        _logQueue.Enqueue(entry);

        if (Interlocked.Increment(ref _queuedCount) <= MaxQueuedEntries) return;
        
        if (_logQueue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queuedCount);
            Interlocked.Increment(ref _droppedCount);
        }
    }

    private async Task ProcessQueue(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var batch = DrainBatch();

            if (batch.Count == 0)
            {
                if (!await DelaySafe(IdlePollMs, token)) break;
                continue;
            }

            bool sent = await SendBatchAsync(batch, token);

            if (sent)
            {
                OnSendSucceeded();
                continue;
            }
            
            if (!token.IsCancellationRequested)
            {
                RequeueBatch(batch);
            }

            if (!await DelaySafe(OnSendFailed(), token)) break;
        }
    }

    private List<core.LogEntry> DrainBatch()
    {
        var batch = new List<core.LogEntry>();

        while (batch.Count < BatchSize && _logQueue.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _queuedCount);
            batch.Add(entry);
        }

        return batch;
    }

    private void RequeueBatch(List<core.LogEntry> batch)
    {
        foreach (var entry in batch)
        {
            _logQueue.Enqueue(entry);

            if (Interlocked.Increment(ref _queuedCount) <= MaxQueuedEntries) continue;

            if (_logQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _queuedCount);
                Interlocked.Increment(ref _droppedCount);
            }
        }
    }

    /// <summary>Returns the backoff to apply after a failed send, and reports the outage once.</summary>
    private int OnSendFailed()
    {
        _consecutiveFailures++;

        if (!_hasReportedOutage)
        {
            _hasReportedOutage = true;
            GD.PrintErr($"[SeqHttpWriter] Cannot reach Seq at {_serverUrl}. Buffering entries and retrying with backoff. " +
                        "Further failures will not be reported until the connection recovers.");
        }

        // 200ms, 400ms, 800ms ... capped, so a dead server does not spin the worker thread.
        int shift = Math.Min(_consecutiveFailures, 8);
        return Math.Min(IdlePollMs << shift, MaxBackoffMs);
    }

    private void OnSendSucceeded()
    {
        if (_hasReportedOutage)
        {
            GD.Print($"[SeqHttpWriter] Connection to {_serverUrl} restored.");
            _hasReportedOutage = false;
        }

        _consecutiveFailures = 0;

        long dropped = Interlocked.Exchange(ref _droppedCount, 0);
        if (dropped > 0)
        {
            GD.PushWarning($"[SeqHttpWriter] Dropped {dropped} log entries while the Seq server was unreachable " +
                           $"(buffer limit is {MaxQueuedEntries} entries).");
        }
    }

    /// <summary>Awaits a delay, returning <c>false</c> when cancellation ended the wait.</summary>
    private static async Task<bool> DelaySafe(int milliseconds, CancellationToken token)
    {
        try
        {
            await Task.Delay(milliseconds, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <returns><c>true</c> if the batch was accepted by the server (or is not worth retrying).</returns>
    private async Task<bool> SendBatchAsync(List<core.LogEntry> batch, CancellationToken token)
    {
        string json;
        
        try
        {
            json = BuildPayload(batch);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SeqHttpWriter] Could not serialize {batch.Count} entries: {e.Message}. Discarding this batch.");
            return true;
        }

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(_apiKey))
            {
                content.Headers.Add("X-Seq-ApiKey", _apiKey);
            }

            using var response = await HttpClient.PostAsync($"{_serverUrl}/api/events/raw", content, token);

            if (response.IsSuccessStatusCode) return true;
            
            if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
            {
                string body = await response.Content.ReadAsStringAsync(token);
                GD.PrintErr($"[SeqHttpWriter] Server rejected {batch.Count} entries with {(int)response.StatusCode} " +
                            $"{(int)response.StatusCode}: {body}. Discarding this batch.");

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    GD.PrintErr("[SeqHttpWriter] Check 'Project Settings -> Addons -> Godot Advanced Logger -> Seq -> Api Key'.");
                }

                return true;
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e)
        {
            if (!_hasReportedOutage)
            {
                GD.PrintErr($"[SeqHttpWriter] Network error: {e.Message}");
            }
            return false;
        }
    }

    private string BuildPayload(List<core.LogEntry> batch)
    {
        var events = new List<object>(batch.Count);

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

            var properties = new Dictionary<string, object>
            {
                { "SessionId", core.LogManager.SessionId },
                { "GameVersion", _gameVersion },
                { "Channel", entry.Channel },
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
                { "MessageTemplate", EscapeMessageTemplate(entry.Message) },
                { "Properties", properties }
            };

            if (entry.Exception != null)
            {
                eventData.Add("Exception", entry.Exception.ToString());
            }

            events.Add(eventData);
        }

        return JsonSerializer.Serialize(new { Events = events }, JsonOptions);
    }

    /// <summary>
    /// Seq parses <c>MessageTemplate</c> as a message template, so a literal brace in a
    /// message (JSON fragments, GDScript dictionaries, code snippets) would be read as a
    /// property placeholder and rendered wrong. Doubling braces escapes them.
    /// </summary>
    private static string EscapeMessageTemplate(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        if (message.IndexOf('{') < 0 && message.IndexOf('}') < 0) return message;

        return message.Replace("{", "{{").Replace("}", "}}");
    }

    public void Shutdown()
    {
        _cts?.Cancel();

        try { _loggingTask?.Wait(500); }
        catch
        {
            // ignored
        }

        var finalBatch = DrainBatch();
        while (_logQueue.TryDequeue(out var extra))
        {
            Interlocked.Decrement(ref _queuedCount);
            finalBatch.Add(extra);
        }

        try
        {
            if (finalBatch.Count == 0) return;
            
            using var flushCts = new CancellationTokenSource(ShutdownFlushTimeout);

            bool flushed = SendBatchAsync(finalBatch, flushCts.Token).Wait(ShutdownFlushTimeout);

            if (!flushed)
            {
                GD.PrintErr($"[SeqHttpWriter] Final flush timed out; {finalBatch.Count} entries were not delivered.");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SeqHttpWriter] Final flush of {finalBatch.Count} entries failed: {e.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
