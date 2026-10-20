#if TOOLS
using System;
using System.Text;
using System.Text.Json;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// Verifies a Seq endpoint from inside the editor by doing exactly what the runtime writer
/// does: posting one event to <c>/api/events/raw</c> with the configured API key.
/// </summary>
/// <remarks>
/// <para>
/// Uses Godot's <see cref="HttpRequest"/> node rather than <c>System.Net.Http</c>: the node
/// is owned by the scene tree and freed with the window, so no background task or static
/// client can outlive a C# assembly reload in the editor.
/// </para>
/// <para>
/// The test is two requests: <c>GET /api</c> identifies the server (and its version) without
/// side effects, then the ingestion request verifies the URL, the API key and ingestion
/// permissions in one go. The test event is clearly labelled so it is easy to find in Seq.
/// </para>
/// </remarks>
internal sealed class SeqConnectionTester
{
    private const double TimeoutSeconds = 6.0;

    public enum Outcome
    {
        Success,
        Unauthorized,
        NotSeq,
        Unreachable,
        Rejected,
        InvalidUrl,
    }

    public sealed record Result(
        Outcome Outcome,
        string Headline,
        string Detail,
        string ServerVersion,
        int StatusCode,
        double ElapsedMs);

    private readonly HttpRequest _http;

    private string _baseUrl;
    private string _apiKey;
    private string _serverVersion;
    private bool _probing;
    private ulong _startTicks;

    public bool IsRunning { get; private set; }

    public event Action<Result> Completed;

    public SeqConnectionTester(Node owner)
    {
        _http = new HttpRequest { Timeout = TimeoutSeconds, UseThreads = true };
        _http.RequestCompleted += OnRequestCompleted;
        owner.AddChild(_http);
    }

    public void Start(string url, string apiKey)
    {
        if (IsRunning) return;

        foreach (var issue in SeqEndpoint.Analyze(url))
        {
            if (issue.Severity != UrlIssueSeverity.Error) continue;
            Finish(new Result(Outcome.InvalidUrl, "Invalid URL", issue.Message, null, 0, 0));
            return;
        }

        _baseUrl = SeqEndpoint.Normalize(url);
        _apiKey = apiKey;
        _serverVersion = null;
        _startTicks = Time.GetTicksMsec();
        IsRunning = true;

        _probing = true;
        var error = _http.Request(_baseUrl + "/api", new[] { "Accept: application/json" });
        if (error != Godot.Error.Ok)
        {
            Finish(new Result(Outcome.Unreachable, "Could not start the request",
                $"Godot reported {error}. Check the URL.", null, 0, 0));
        }
    }

    public void Cancel()
    {
        if (!IsRunning) return;
        _http.CancelRequest();
        IsRunning = false;
    }

    private void OnRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        if (!IsRunning) return;

        if (_probing)
        {
            _probing = false;
            OnProbeCompleted((HttpRequest.Result)result, (int)responseCode, body);
            return;
        }

        OnIngestCompleted((HttpRequest.Result)result, (int)responseCode, body);
    }

    private void OnProbeCompleted(HttpRequest.Result result, int code, byte[] body)
    {
        if (result != HttpRequest.Result.Success)
        {
            Finish(Unreachable(result));
            return;
        }

        // A 401 here only means the UI requires authentication; ingestion may still work
        // with an API key, so carry on. Anything that answers 200 should look like Seq.
        if (code == 200)
        {
            _serverVersion = TryReadVersion(body);
            if (_serverVersion == null && !LooksLikeSeq(body))
            {
                Finish(new Result(Outcome.NotSeq, "This does not look like a Seq server",
                    $"{_baseUrl}/api answered, but not with Seq's API description. " +
                    "Check host and port (Seq's default is 5341).", null, code, Elapsed()));
                return;
            }
        }

        string payload = BuildTestEvent();
        var headers = string.IsNullOrEmpty(_apiKey)
            ? new[] { "Content-Type: application/json" }
            : new[] { "Content-Type: application/json", "X-Seq-ApiKey: " + _apiKey };

        var error = _http.Request(SeqEndpoint.GetIngestUrl(_baseUrl), headers, HttpClient.Method.Post, payload);
        if (error != Godot.Error.Ok)
        {
            Finish(new Result(Outcome.Unreachable, "Could not send the test event",
                $"Godot reported {error}.", _serverVersion, 0, Elapsed()));
        }
    }

    private void OnIngestCompleted(HttpRequest.Result result, int code, byte[] body)
    {
        if (result != HttpRequest.Result.Success)
        {
            Finish(Unreachable(result));
            return;
        }

        string server = _serverVersion != null ? $"Seq {_serverVersion}" : "Seq";

        switch (code)
        {
            case >= 200 and < 300:
                Finish(new Result(Outcome.Success, $"Connected to {server}",
                    "A test event was accepted. Find it in Seq with: Channel = 'GodotAdvancedLogger'",
                    _serverVersion, code, Elapsed()));
                break;

            case 401 or 403:
                Finish(new Result(Outcome.Unauthorized,
                    string.IsNullOrEmpty(_apiKey) ? "Seq requires an API key" : "The API key was rejected",
                    string.IsNullOrEmpty(_apiKey)
                        ? "This server does not accept unauthenticated events. Create an API key in Seq " +
                          "(Settings → API Keys) and paste it below."
                        : "Check that the key is correct, not revoked, and has the Ingest permission.",
                    _serverVersion, code, Elapsed()));
                break;

            case 404:
                Finish(new Result(Outcome.NotSeq, "Ingestion endpoint not found",
                    $"{SeqEndpoint.GetIngestUrl(_baseUrl)} returned 404. Is this the Seq server, " +
                    "and is the base URL correct?", _serverVersion, code, Elapsed()));
                break;

            default:
                Finish(new Result(Outcome.Rejected, $"{server} rejected the test event (HTTP {code})",
                    Truncate(Encoding.UTF8.GetString(body ?? Array.Empty<byte>()), 300),
                    _serverVersion, code, Elapsed()));
                break;
        }
    }

    private Result Unreachable(HttpRequest.Result result)
    {
        string detail = result switch
        {
            HttpRequest.Result.CantResolve => "The host name could not be resolved.",
            HttpRequest.Result.CantConnect => "Nothing is listening at this address. Is Seq running?",
            HttpRequest.Result.Timeout => $"No answer within {TimeoutSeconds:0} seconds.",
            HttpRequest.Result.TlsHandshakeError => "The TLS handshake failed. Check the certificate, or try http:// for a local server.",
            HttpRequest.Result.ConnectionError => "The connection was interrupted.",
            _ => $"Request failed ({result})."
        };

        return new Result(Outcome.Unreachable, $"Cannot reach {_baseUrl}", detail, null, 0, Elapsed());
    }

    private static string BuildTestEvent()
    {
        var payload = new
        {
            Events = new[]
            {
                new
                {
                    Timestamp = DateTime.Now.ToString("O"),
                    Level = "Information",
                    MessageTemplate = "Connection test from the Godot editor ({Project})",
                    Properties = new
                    {
                        Project = ProjectSettings.GetSetting("application/config/name", "Godot project").AsString(),
                        Channel = "GodotAdvancedLogger",
                        Source = "EditorSettingsWindow",
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string TryReadVersion(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Version", out var version)
                && version.ValueKind == JsonValueKind.String)
            {
                return version.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON: handled by LooksLikeSeq.
        }

        return null;
    }

    private static bool LooksLikeSeq(byte[] body)
    {
        string text = Encoding.UTF8.GetString(body ?? Array.Empty<byte>());
        return text.Contains("Seq", StringComparison.OrdinalIgnoreCase)
               || text.Contains("\"Links\"", StringComparison.Ordinal);
    }

    private double Elapsed() => Time.GetTicksMsec() - _startTicks;

    private void Finish(Result result)
    {
        IsRunning = false;
        _probing = false;
        Completed?.Invoke(result);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
#endif
