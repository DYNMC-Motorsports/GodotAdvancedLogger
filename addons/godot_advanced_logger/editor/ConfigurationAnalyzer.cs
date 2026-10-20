#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

internal enum IssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>A finding about the current configuration, optionally with a one-click fix.</summary>
internal sealed record ConfigIssue(
    IssueSeverity Severity,
    string PageId,
    string Title,
    string Detail,
    string FixLabel = null,
    Action Fix = null);

/// <summary>
/// Looks at the configuration as a whole and points out combinations that are invalid,
/// surprising or easy to get wrong. Every finding names the page that fixes it.
/// </summary>
internal static class ConfigurationAnalyzer
{
    public static List<ConfigIssue> Analyze(SettingsStore store)
    {
        var issues = new List<ConfigIssue>();

        AnalyzeOutputs(store, issues);
        AnalyzeSeq(store, issues);
        AnalyzeLevels(store, issues);
        AnalyzeOverlay(store, issues);
        AnalyzeChannels(store, issues);

        return issues.OrderByDescending(i => i.Severity).ToList();
    }

    private static void AnalyzeOutputs(SettingsStore store, List<ConfigIssue> issues)
    {
        bool console = store.GetBool(LoggerSettings.EnableConsole);
        bool file = store.GetBool(LoggerSettings.EnableFile);
        bool json = store.GetBool(LoggerSettings.EnableJson);
        bool seq = store.GetBool(LoggerSettings.EnableSeq);
        bool overlay = store.GetBool(LoggerSettings.EnableOverlay);
        bool overlayInRelease = store.GetBool(LoggerSettings.OverlayAllowInRelease);

        if (!console && !file && !json && !seq && !overlay)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error, OutputsPage.PageId,
                "No output is enabled",
                "Every entry is discarded. Enable at least one output.",
                "Enable console",
                () => store.SetBool(LoggerSettings.EnableConsole, true)));
            return;
        }

        bool releaseHasOutput = console || file || json || (overlay && overlayInRelease);
        if (!releaseHasOutput)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Warning, OutputsPage.PageId,
                "Release exports log nowhere",
                "Seq and the in-game overlay are debug-only, so a release export has no active output. " +
                "Enable the console or a file output if you want logs from shipped builds."));
        }
    }

    private static void AnalyzeSeq(SettingsStore store, List<ConfigIssue> issues)
    {
        if (!store.GetBool(LoggerSettings.EnableSeq)) return;

        string url = store.GetString(LoggerSettings.SeqServerUrl);
        bool hasKey = !string.IsNullOrEmpty(store.GetString(LoggerSettings.SeqApiKey));

        foreach (var urlIssue in SeqEndpoint.Analyze(url, hasKey))
        {
            if (urlIssue.Severity == UrlIssueSeverity.Info) continue;

            string suggested = urlIssue.SuggestedUrl;
            issues.Add(new ConfigIssue(
                urlIssue.Severity == UrlIssueSeverity.Error ? IssueSeverity.Error : IssueSeverity.Warning,
                SeqPage.PageId,
                urlIssue.Severity == UrlIssueSeverity.Error ? "Seq URL is invalid" : "Seq URL needs attention",
                urlIssue.Message,
                suggested != null ? "Use " + suggested : null,
                suggested != null ? () => store.SetString(LoggerSettings.SeqServerUrl, suggested) : null));
        }
    }

    private static void AnalyzeLevels(SettingsStore store, List<ConfigIssue> issues)
    {
        var release = (LogLevel)store.GetInt(LoggerSettings.MinLogLevelRelease);

        if (release == LogLevel.Debug)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Info, LevelsPage.PageId,
                "Release level \"Debug\" behaves like \"Info\"",
                "Debug calls are compiled out of release exports (C#) or return early (GDScript), " +
                "so no Debug entry can reach the logger there.",
                "Set to Info",
                () => store.SetInt(LoggerSettings.MinLogLevelRelease, (int)LogLevel.Info)));
        }
    }

    private static void AnalyzeOverlay(SettingsStore store, List<ConfigIssue> issues)
    {
        string hotkey = store.GetString(LoggerSettings.OverlayHotkey);

        if (!LoggerSettings.TryParseHotkey(hotkey, out var key))
        {
            issues.Add(new ConfigIssue(IssueSeverity.Warning, OverlayPage.PageId,
                "Overlay hotkey is not a valid key",
                $"\"{hotkey}\" is not a Godot key name, so the overlay falls back to F12.",
                "Reset to F12",
                () => store.ResetToDefault(LoggerSettings.OverlayHotkey)));
        }
        else if (OverlayPage.IsTypingKey(key))
        {
            issues.Add(new ConfigIssue(IssueSeverity.Info, OverlayPage.PageId,
                $"Overlay hotkey \"{OS.GetKeycodeString(key)}\" is a typing key",
                "It is ignored while a text field in the overlay has focus, and your game may use it too. " +
                "Function keys avoid both problems."));
        }

        if (store.GetBool(LoggerSettings.OverlayAllowInRelease))
        {
            issues.Add(new ConfigIssue(IssueSeverity.Warning, OverlayPage.PageId,
                "Players can open the debug overlay",
                "The overlay, including every console command you registered, is available in release exports.",
                "Disable in release",
                () => store.SetBool(LoggerSettings.OverlayAllowInRelease, false)));
        }
    }

    private static void AnalyzeChannels(SettingsStore store, List<ConfigIssue> issues)
    {
        string raw = store.GetString(LoggerSettings.MutedChannels);
        var parsed = LoggerSettings.ParseChannelList(raw);
        string canonical = LoggerSettings.FormatChannelList(parsed);

        if (!string.IsNullOrWhiteSpace(raw) && raw != canonical)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Info, ChannelsPage.PageId,
                "Muted channel list can be tidied up",
                "It contains empty entries, duplicates or stray spaces. They are ignored at runtime.",
                "Tidy up",
                () => store.SetString(LoggerSettings.MutedChannels, canonical)));
        }

        if (parsed.Any(c => string.Equals(c, LogManager.DefaultChannel, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new ConfigIssue(IssueSeverity.Info, ChannelsPage.PageId,
                $"The default channel \"{LogManager.DefaultChannel}\" is muted",
                "Every entry logged without an explicit channel is dropped."));
        }
    }
}
#endif
