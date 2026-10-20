using System;
using System.Collections.Generic;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

/// <summary>
/// Single source of truth for every Project Setting the logger owns: its path, type,
/// default value, inspector hint and a short human-readable summary.
/// </summary>
/// <remarks>
/// <para>
/// The editor plugin registers the settings from <see cref="All"/>, the editor settings
/// window builds its controls from the same definitions, and the runtime reads values
/// through the typed getters below. Add a setting here once and every consumer sees it.
/// </para>
/// <para>
/// The getters fall back to the <em>schema default</em> when a setting is absent.
/// That matters in exported games: Godot only writes values that differ from their
/// default into <c>project.godot</c>, so a setting the developer never touched does
/// not exist at runtime at all.
/// </para>
/// </remarks>
public static class LoggerSettings
{
    /// <summary>Common prefix of every setting path owned by this plugin.</summary>
    public const string Root = "addons/godot_advanced_logger";

    /// <summary>Directory the file writers log into.</summary>
    public const string LogDirectory = "user://logs/godotadvancedlogger";

    /// <summary>Separator used to store the muted channel list as a single string.</summary>
    public const char ChannelSeparator = ',';

    // ---------------------------------------------------------------------
    // Definitions
    // ---------------------------------------------------------------------

    public static readonly SettingDefinition MinLogLevel = new(
        Root + "/settings/min_log_level", Variant.Type.Int, (int)LogLevel.Debug,
        PropertyHint.Enum, LevelHintString(),
        "Minimum level (Editor & debug builds)",
        "Entries below this level are dropped when running from the editor or in a debug export.");

    public static readonly SettingDefinition MinLogLevelRelease = new(
        Root + "/settings/min_log_level_release", Variant.Type.Int, (int)LogLevel.Info,
        PropertyHint.Enum, LevelHintString(),
        "Minimum level (Release exports)",
        "Entries below this level are dropped in exports built without debug.");

    public static readonly SettingDefinition MutedChannels = new(
        Root + "/settings/muted_channels", Variant.Type.String, "",
        PropertyHint.PlaceholderText, "Comma separated list (e.g. Physics, AI)",
        "Muted channels",
        "Entries on these channels are dropped before they reach any output.");

    public static readonly SettingDefinition OverlayHotkey = new(
        Root + "/settings/ingame_ui_hotkey", Variant.Type.String, "F12",
        PropertyHint.PlaceholderText, "Key name, e.g. F12",
        "Overlay hotkey",
        "Key that toggles the in-game debug overlay.");

    public static readonly SettingDefinition OverlayPauseGame = new(
        Root + "/settings/ingame_ui_pause_game", Variant.Type.Bool, false,
        PropertyHint.None, "",
        "Pause game while overlay is open",
        "Pauses the SceneTree while the debug overlay is visible.");

    public static readonly SettingDefinition OverlayAllowInRelease = new(
        Root + "/settings/ingame_ui_allow_in_release", Variant.Type.Bool, false,
        PropertyHint.None, "",
        "Allow overlay in release exports",
        "Lets players open the debug overlay in exports built without debug.");

    public static readonly SettingDefinition EnableOverlay = new(
        Root + "/writers/enable_overlay", Variant.Type.Bool, true,
        PropertyHint.None, "",
        "In-game overlay",
        "Streams entries into the Logs tab of the in-game debug overlay.");

    public static readonly SettingDefinition EnableConsole = new(
        Root + "/writers/enable_console", Variant.Type.Bool, true,
        PropertyHint.None, "",
        "Godot console",
        "Prints colored entries to the Output panel and the terminal.");

    public static readonly SettingDefinition EnableFile = new(
        Root + "/writers/enable_file", Variant.Type.Bool, false,
        PropertyHint.None, "",
        "Text file",
        "Writes human-readable .txt log files.");

    public static readonly SettingDefinition EnableJson = new(
        Root + "/writers/enable_json", Variant.Type.Bool, false,
        PropertyHint.None, "",
        "JSON Lines file",
        "Writes one JSON object per line (.jsonl) for tools and scripts.");

    public static readonly SettingDefinition EnableSeq = new(
        Root + "/writers/enable_seq", Variant.Type.Bool, false,
        PropertyHint.None, "",
        "Seq server",
        "Streams entries to a Seq server in real time.");

    public static readonly SettingDefinition SeqServerUrl = new(
        Root + "/seq/server_url", Variant.Type.String, SeqEndpoint.DefaultBaseUrl,
        PropertyHint.PlaceholderText, SeqEndpoint.DefaultBaseUrl,
        "Seq server URL",
        "Base URL of the Seq server, without the /api/events/raw suffix.");

    public static readonly SettingDefinition SeqApiKey = new(
        Root + "/seq/api_key", Variant.Type.String, "",
        PropertyHint.Password, "Optional",
        "Seq API key",
        "Optional key sent as the X-Seq-ApiKey header.");

    /// <summary>Every setting, in the order it is registered and shown in Project Settings.</summary>
    public static readonly IReadOnlyList<SettingDefinition> All = new[]
    {
        MinLogLevel, MinLogLevelRelease, MutedChannels,
        OverlayHotkey, OverlayPauseGame, OverlayAllowInRelease,
        EnableConsole, EnableOverlay, EnableFile, EnableJson, EnableSeq,
        SeqServerUrl, SeqApiKey,
    };

    // ---------------------------------------------------------------------
    // Raw access
    // ---------------------------------------------------------------------

    public static bool GetBool(SettingDefinition setting) =>
        ProjectSettings.HasSetting(setting.Path)
            ? ProjectSettings.GetSetting(setting.Path).AsBool()
            : (bool)setting.DefaultValue;

    public static int GetInt(SettingDefinition setting) =>
        ProjectSettings.HasSetting(setting.Path)
            ? ProjectSettings.GetSetting(setting.Path).AsInt32()
            : (int)setting.DefaultValue;

    public static string GetString(SettingDefinition setting) =>
        ProjectSettings.HasSetting(setting.Path)
            ? ProjectSettings.GetSetting(setting.Path).AsString()
            : (string)setting.DefaultValue;

    // ---------------------------------------------------------------------
    // Typed accessors used by the runtime
    // ---------------------------------------------------------------------

    /// <summary>The minimum level for the current build type, falling back to the schema default.</summary>
    public static LogLevel GetMinLogLevel(bool isDebugBuild)
    {
        int raw = GetInt(isDebugBuild ? MinLogLevel : MinLogLevelRelease);

        return Enum.IsDefined(typeof(LogLevel), raw)
            ? (LogLevel)raw
            : (LogLevel)(int)(isDebugBuild ? MinLogLevel : MinLogLevelRelease).DefaultValue;
    }

    /// <summary>The muted channels, trimmed and de-duplicated (case-insensitive).</summary>
    public static IReadOnlyList<string> GetMutedChannels() => ParseChannelList(GetString(MutedChannels));

    /// <summary>The overlay hotkey, or <see cref="Key.F12"/> if the stored value cannot be parsed.</summary>
    public static Key GetOverlayHotkey(out bool parsed)
    {
        parsed = TryParseHotkey(GetString(OverlayHotkey), out var key);
        return parsed ? key : Key.F12;
    }

    // ---------------------------------------------------------------------
    // Helpers shared by the runtime and the editor
    // ---------------------------------------------------------------------

    /// <summary>
    /// Splits a stored channel list into clean names: trimmed, without empty entries and
    /// without case-insensitive duplicates (channel matching is case-insensitive).
    /// </summary>
    public static IReadOnlyList<string> ParseChannelList(string raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in raw.Split(ChannelSeparator))
        {
            string name = part.Trim();
            if (name.Length == 0 || !seen.Add(name)) continue;
            result.Add(name);
        }

        return result;
    }

    /// <summary>Joins channel names into the stored representation, e.g. <c>"Physics, AI"</c>.</summary>
    public static string FormatChannelList(IEnumerable<string> channels) =>
        string.Join(ChannelSeparator + " ", ParseChannelList(string.Join(ChannelSeparator, channels)));

    /// <summary>Parses a key name such as <c>"F12"</c> or <c>"QuoteLeft"</c> (case-insensitive).</summary>
    public static bool TryParseHotkey(string value, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Enum.TryParse also accepts numbers ("4194343"); only names are meaningful here.
        string trimmed = value.Trim();
        if (char.IsDigit(trimmed[0]) && trimmed.Length > 1) return false;

        if (!Enum.TryParse(trimmed, true, out key) || key == Key.None || !Enum.IsDefined(typeof(Key), key))
        {
            key = Key.None;
            return false;
        }

        return true;
    }

    private static string LevelHintString() => string.Join(",", Enum.GetNames(typeof(LogLevel)));
}

/// <summary>
/// Describes one Project Setting owned by the logger.
/// </summary>
/// <param name="Path">Full ProjectSettings path.</param>
/// <param name="Type">Variant type stored in <c>project.godot</c>.</param>
/// <param name="DefaultValue">Default as a plain CLR value (<see cref="bool"/>, <see cref="int"/> or <see cref="string"/>).</param>
/// <param name="Hint">Inspector hint used in the stock Project Settings dialog.</param>
/// <param name="HintString">Hint string matching <paramref name="Hint"/>.</param>
/// <param name="Label">Short display name.</param>
/// <param name="Summary">One-sentence, plain-text explanation.</param>
public sealed record SettingDefinition(
    string Path,
    Variant.Type Type,
    object DefaultValue,
    PropertyHint Hint,
    string HintString,
    string Label,
    string Summary)
{
    /// <summary>The default value as a <see cref="Variant"/>.</summary>
    public Variant DefaultVariant => DefaultValue switch
    {
        bool b => Variant.CreateFrom(b),
        int i => Variant.CreateFrom(i),
        string s => Variant.CreateFrom(s),
        _ => throw new InvalidOperationException($"Unsupported default type for '{Path}'.")
    };
}
