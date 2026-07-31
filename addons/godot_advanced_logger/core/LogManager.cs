using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

public partial class LogManager : Node
{
    /// <summary>
    /// Channel used by the static convenience methods when the caller does not specify one.
    /// </summary>
    public const string DefaultChannel = "General";

    public static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];

    public static LogManager Instance { get; private set; }

    private readonly object _writersLock = new();
    private readonly List<writers.ILogWriter> _writers = new();

    /// <summary>
    /// Immutable snapshot of <see cref="_writers"/> used by the logging hot path.
    /// Swapping a whole array under a lock lets writers be added or removed at runtime
    /// without risking an <see cref="InvalidOperationException"/> mid-iteration, and
    /// without taking a lock for every single log call.
    /// </summary>
    private volatile writers.ILogWriter[] _writerSnapshot = Array.Empty<writers.ILogWriter>();

    private readonly HashSet<string> _mutedChannels = new(StringComparer.OrdinalIgnoreCase);

    private LogLevel _minLogLevel = LogLevel.Debug;

    private const string SettingMutedChannels = "addons/godot_advanced_logger/settings/muted_channels";
    private const string SettingLogLevel = "addons/godot_advanced_logger/settings/min_log_level";
    private const string SettingLogLevelRelease = "addons/godot_advanced_logger/settings/min_log_level_release";

    public bool IsGlobalEnabled { get; set; } = true;

    public override void _EnterTree()
    {
        if (Instance != null)
        {
            QueueFree();
            return;
        }
        Instance = this;

        LoadSettings();
        InitializeWriters();
    }

    private void InitializeWriters()
    {
        bool GetBoolSetting(string path, bool def) =>
            ProjectSettings.HasSetting(path) ? ProjectSettings.GetSetting(path).AsBool() : def;

        bool isDebugBuild = OS.IsDebugBuild();

        if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_console", true))
        {
            AddWriter(new writers.ConsoleWriter());
        }

        if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_file", false))
        {
            AddWriter(new writers.FileWriter());
        }

        if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_json", false))
        {
            AddWriter(new writers.JsonFileWriter());
        }

        if (isDebugBuild)
        {
            if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_seq", false))
            {
                string seqUrl = ProjectSettings.HasSetting("addons/godot_advanced_logger/seq/server_url")
                    ? ProjectSettings.GetSetting("addons/godot_advanced_logger/seq/server_url").AsString()
                    : "http://localhost:5341";

                string seqKey = ProjectSettings.HasSetting("addons/godot_advanced_logger/seq/api_key")
                    ? ProjectSettings.GetSetting("addons/godot_advanced_logger/seq/api_key").AsString()
                    : null;

                AddWriter(new writers.SeqHttpWriter(seqUrl, seqKey));
            }

            if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_overlay", true))
            {
                AddWriter(new writers.DebugScreenWriter());
            }
        }
        else
        {
            GD.Print("[GodotAdvancedLogger] Release Build detected. Dev-Tools (Seq, InGameUI) are hard-disabled.");
        }
    }

    private void LoadSettings()
    {
        bool isDebug = OS.IsDebugBuild();

        string activeSettingPath = isDebug ? SettingLogLevel : SettingLogLevelRelease;

        if (!ProjectSettings.HasSetting(activeSettingPath))
        {
            activeSettingPath = SettingLogLevel;
        }

        if (ProjectSettings.HasSetting(SettingMutedChannels))
        {
            string rawString = ProjectSettings.GetSetting(SettingMutedChannels).AsString();
            if (!string.IsNullOrWhiteSpace(rawString))
            {
                var channels = rawString.Split(',');
                foreach (var channel in channels)
                {
                    MuteChannel(channel.Trim());
                }
                GD.Print($"[LogManager] Loaded muted channels: {string.Join(", ", _mutedChannels)}");
            }
        }

        if (ProjectSettings.HasSetting(activeSettingPath))
        {
            int levelInt = ProjectSettings.GetSetting(activeSettingPath).AsInt32();
            if (Enum.IsDefined(typeof(LogLevel), levelInt))
            {
                _minLogLevel = (LogLevel)levelInt;

                GD.Print($"[LogManager] Configured for {(isDebug ? "Debug" : "Release")}. Min Level: {_minLogLevel}");
            }
        }
    }

    public override void _ExitTree()
    {
        if (Instance != this) return;

        Shutdown();
        Instance = null;
    }

    public void AddWriter(writers.ILogWriter writer)
    {
        if (writer == null) return;

        writer.Initialize();

        lock (_writersLock)
        {
            _writers.Add(writer);
            _writerSnapshot = _writers.ToArray();
        }
    }

    /// <summary>
    /// Removes a previously added writer and shuts it down.
    /// </summary>
    /// <returns><c>true</c> if the writer was registered and has been removed.</returns>
    public bool RemoveWriter(writers.ILogWriter writer)
    {
        if (writer == null) return false;

        lock (_writersLock)
        {
            if (!_writers.Remove(writer)) return false;
            _writerSnapshot = _writers.ToArray();
        }

        writer.Shutdown();
        return true;
    }

    public void MuteChannel(string channel) => _mutedChannels.Add(channel);
    public void UnmuteChannel(string channel) => _mutedChannels.Remove(channel);

    public static bool IsLevelEnabled(LogLevel level, string channel)
    {
        if (Instance == null || !Instance.IsGlobalEnabled) return false;
        if (level < Instance._minLogLevel) return false;
        if (Instance._mutedChannels.Contains(channel)) return false;

        return true;
    }

#nullable enable

    // ---------------------------------------------------------------------
    // Core entry point
    // ---------------------------------------------------------------------

    /// <summary>
    /// Writes a log entry to every registered writer.
    /// </summary>
    /// <remarks>
    /// The <c>[Caller*]</c> parameters are placed last so that <paramref name="context"/>
    /// and <paramref name="ex"/> can be passed positionally. Prefer the
    /// <c>Info</c> / <c>Warning</c> / <c>Error</c> / <c>Debug</c> helpers below, or a
    /// <see cref="ContextLogger"/>, over calling this directly.
    /// </remarks>
    public static void Write(
        LogLevel level,
        string channel,
        string message,
        Dictionary<string, object>? context = null,
        Exception? ex = null,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0)
    {
        if (!IsLevelEnabled(level, channel)) return;

        var instance = Instance;
        if (instance == null) return;

        var entry = new LogEntry(
            DateTime.Now,
            level,
            channel,
            message,
            callerFilePath,
            callerMemberName,
            callerLineNumber,
            context,
            ex);

        // Read the snapshot once: AddWriter/RemoveWriter swap in a new array rather
        // than mutating this one, so the loop below is safe even if it races with them.
        var snapshot = instance._writerSnapshot;
        for (int i = 0; i < snapshot.Length; i++)
        {
            snapshot[i].Write(in entry);
        }
    }

    // ---------------------------------------------------------------------
    // Convenience helpers
    // ---------------------------------------------------------------------

    /// <summary>Logs at <see cref="LogLevel.Info"/>.</summary>
    public static void Info(
        string message,
        Dictionary<string, object>? context = null,
        string channel = DefaultChannel,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0)
        => Write(LogLevel.Info, channel, message, context, null, callerFilePath, callerMemberName, callerLineNumber);

    /// <summary>Logs at <see cref="LogLevel.Warning"/>.</summary>
    public static void Warning(
        string message,
        Dictionary<string, object>? context = null,
        string channel = DefaultChannel,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0)
        => Write(LogLevel.Warning, channel, message, context, null, callerFilePath, callerMemberName, callerLineNumber);

    /// <summary>Logs at <see cref="LogLevel.Error"/>, optionally attaching an exception.</summary>
    public static void Error(
        string message,
        Exception? ex = null,
        Dictionary<string, object>? context = null,
        string channel = DefaultChannel,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0)
        => Write(LogLevel.Error, channel, message, context, ex, callerFilePath, callerMemberName, callerLineNumber);

    /// <summary>
    /// Logs at <see cref="LogLevel.Debug"/>. Calls to this method are removed entirely
    /// by the compiler in builds where <c>DEBUG</c> is not defined, so neither the
    /// arguments nor the call itself cost anything in a release export.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Debug(
        string message,
        Dictionary<string, object>? context = null,
        string channel = DefaultChannel,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0)
        => Write(LogLevel.Debug, channel, message, context, null, callerFilePath, callerMemberName, callerLineNumber);

    // ---------------------------------------------------------------------
    // Deprecated
    // ---------------------------------------------------------------------

    [Obsolete("Use LogManager.Write(level, channel, message, context, ex) or the Info/Warning/Error/Debug helpers. " +
              "This overload places the [Caller*] parameters before 'context', which makes 'context' impossible to pass positionally.")]
    public static void Log(
        LogLevel level,
        string channel,
        string message,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0,
        Dictionary<string, object>? context = null,
        Exception? ex = null)
        => Write(level, channel, message, context, ex, callerFilePath, callerMemberName, callerLineNumber);
#nullable restore

    private void Shutdown()
    {
        writers.ILogWriter[] snapshot;

        lock (_writersLock)
        {
            snapshot = _writers.ToArray();
            _writers.Clear();
            _writerSnapshot = Array.Empty<writers.ILogWriter>();
        }

        foreach (var writer in snapshot)
        {
            try
            {
                writer.Shutdown();
            }
            catch (Exception e)
            {
                // One misbehaving writer must not prevent the others from flushing.
                GD.PrintErr($"[LogManager] Writer '{writer.GetType().Name}' failed to shut down: {e.Message}");
            }
        }
    }
}
