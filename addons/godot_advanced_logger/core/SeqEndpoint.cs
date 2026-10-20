using System;
using System.Collections.Generic;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

/// <summary>
/// Normalizes and validates Seq server URLs. Shared by <c>SeqHttpWriter</c> (which needs a
/// clean base URL) and the editor settings window (which explains what is wrong with one).
/// </summary>
public static class SeqEndpoint
{
    public const string DefaultBaseUrl = "http://localhost:5341";

    /// <summary>Path of Seq's raw ingestion API, appended to the base URL.</summary>
    public const string IngestPath = "/api/events/raw";

    /// <summary>
    /// Turns whatever the developer typed into a base URL the writer can append
    /// <see cref="IngestPath"/> to: trims whitespace and trailing slashes, and strips a
    /// pasted ingestion path (<c>/api/events/raw</c>, optionally with <c>?clef</c>) or a bare <c>/api</c>.
    /// </summary>
    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return DefaultBaseUrl;

        string result = url.Trim();

        int query = result.IndexOfAny(new[] { '?', '#' });
        if (query >= 0 && EndsWithApiPath(result[..query])) result = result[..query];

        result = result.TrimEnd('/');

        if (result.EndsWith(IngestPath, StringComparison.OrdinalIgnoreCase))
            result = result[..^IngestPath.Length];
        else if (result.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            result = result[..^"/api".Length];

        return result.TrimEnd('/');
    }

    /// <summary>The full URL events are posted to for a given (possibly un-normalized) base URL.</summary>
    public static string GetIngestUrl(string url) => Normalize(url) + IngestPath;

    /// <summary>
    /// Checks a URL as typed and returns every problem found, most severe first.
    /// An empty list means the URL is fine.
    /// </summary>
    public static IReadOnlyList<UrlIssue> Analyze(string url, bool hasApiKey = false)
    {
        var issues = new List<UrlIssue>();

        if (string.IsNullOrWhiteSpace(url))
        {
            issues.Add(new UrlIssue(UrlIssueSeverity.Error,
                "The URL is empty. The writer would fall back to " + DefaultBaseUrl + ".",
                DefaultBaseUrl));
            return issues;
        }

        string trimmed = url.Trim();

        if (trimmed != url)
        {
            issues.Add(new UrlIssue(UrlIssueSeverity.Info,
                "Leading or trailing whitespace will be ignored.", trimmed));
        }

        bool hasScheme = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                         || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (!hasScheme)
        {
            bool otherScheme = trimmed.Contains("://", StringComparison.Ordinal);
            issues.Add(new UrlIssue(UrlIssueSeverity.Error,
                otherScheme
                    ? "Only http:// and https:// are supported."
                    : "The URL needs a scheme, e.g. http://.",
                otherScheme ? null : Normalize("http://" + trimmed.TrimStart('/'))));
            return issues;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            issues.Add(new UrlIssue(UrlIssueSeverity.Error, "This is not a valid URL.", null));
            return issues;
        }

        string normalized = Normalize(trimmed);
        string path = uri.AbsolutePath.TrimEnd('/');

        if (path.EndsWith(IngestPath, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new UrlIssue(UrlIssueSeverity.Warning,
                "Enter the server's base URL only. The logger appends " + IngestPath +
                " itself; the extra path is stripped at runtime.",
                normalized));
        }
        else if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            issues.Add(new UrlIssue(UrlIssueSeverity.Warning,
                "Query strings and fragments are not supported in the base URL.",
                uri.GetLeftPart(UriPartial.Path).TrimEnd('/')));
        }

        if (hasApiKey && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            issues.Add(new UrlIssue(UrlIssueSeverity.Info,
                "The API key is sent unencrypted over http:// to a non-local server. Prefer https://.",
                null));
        }

        issues.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        return issues;
    }

    private static bool EndsWithApiPath(string value)
    {
        string trimmed = value.TrimEnd('/');
        return trimmed.EndsWith(IngestPath, StringComparison.OrdinalIgnoreCase)
               || trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase);
    }
}

public enum UrlIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>A problem found in a Seq URL.</summary>
/// <param name="Severity">How serious the problem is; <see cref="UrlIssueSeverity.Error"/> means the writer cannot connect.</param>
/// <param name="Message">Explanation for the developer.</param>
/// <param name="SuggestedUrl">A corrected URL, or <c>null</c> if there is no automatic fix.</param>
public sealed record UrlIssue(UrlIssueSeverity Severity, string Message, string SuggestedUrl);
