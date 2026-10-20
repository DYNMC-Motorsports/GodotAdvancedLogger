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
        bool isDebugBuild = OS.IsDebugBuild();

        if (LoggerSettings.GetBool(LoggerSettings.EnableConsole))
        {
            AddWriter(new writers.ConsoleWriter());
        }

        if (LoggerSettings.GetBool(LoggerSettings.EnableFile))
        {
            AddWriter(new writers.FileWriter());
        }

        if (LoggerSettings.GetBool(LoggerSettings.EnableJson))
        {
            AddWriter(new writers.JsonFileWriter());
        }

        // Seq is a developer tool that phones home over the network: debug builds only.
        if (isDebugBuild && LoggerSettings.GetBool(LoggerSettings.EnableSeq))
        {
            AddWriter(new writers.SeqHttpWriter(
                LoggerSettings.GetString(LoggerSettings.SeqServerUrl),
                LoggerSettings.GetString(LoggerSettings.SeqApiKey)));
        }

        // The overlay follows the same gate as DebugScreen itself, so a release build that
        // opts into the overlay also gets entries in its Logs tab.
        bool overlayAvailable = isDebugBuild || LoggerSettings.GetBool(LoggerSettings.OverlayAllowInRelease);

        if (overlayAvailable && LoggerSettings.GetBool(LoggerSettings.EnableOverlay))
        {
            AddWriter(new writers.DebugScreenWriter());
        }

        if (!isDebugBuild)
        {
            GD.Print(overlayAvailable
                ? "[GodotAdvancedLogger] Release build detected. Seq is hard-disabled; the in-game overlay is enabled by project setting."
                : "[GodotAdvancedLogger] Release build detected. Dev-Tools (Seq, InGameUI) are hard-disabled.");
        }
    }

    private void LoadSettings()
    {
        bool isDebug = OS.IsDebugBuild();

        foreach (var channel in LoggerSettings.GetMutedChannels())
        {
            MuteChannel(channel);
        }

        if (_mutedChannels.Count > 0)
        {
            GD.Print($"[LogManager] Loaded muted channels: {string.Join(", ", _mutedChannels)}");
        }

        // Falls back to the schema default when the setting is absent, which is the normal
        // case in exports: Godot does not write default values into project.godot.
        _minLogLevel = LoggerSettings.GetMinLogLevel(isDebug);

        GD.Print($"[LogManager] Configured for {(isDebug ? "Debug" : "Release")}. Min Level: {_minLogLevel}");
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

    /// <summary>Whether <paramref name="channel"/> is currently muted (case-insensitive).</summary>
    public bool IsChannelMuted(string channel) => channel != null && _mutedChannels.Contains(channel);

    /// <summary>
    /// Minimum level that will be written. Loaded from Project Settings at startup and
    /// adjustable at runtime - the in-game console exposes this as the <c>loglevel</c> command.
    /// </summary>
    public LogLevel MinLogLevel
    {
        get => _minLogLevel;
        set => _minLogLevel = value;
    }

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

    // ---------------------------------------------------------------------
    // GDScript bridge
    // ---------------------------------------------------------------------
    //
    // GDScript can only call methods whose parameters are all Variant-marshallable, and it
    // cannot call static methods at all, but it can call public instance methods of the LogManager.
    // These are the entry points the GodotLogger.gd facade forwards to;
    // they are not meant to be called from C#.
    //
    // LogLevel crosses as int and context as a Godot Dictionary. Caller file, member and
    // line are passed explicitly because [CallerFilePath] and friends are filled in by the
    // C# compiler at the call site, and GDScript has no equivalent - the facade supplies
    // them from get_stack() instead.

    /// <summary>
    /// GDScript entry point. Prefer <c>GodotLogger.info(...)</c> over calling this directly.
    /// </summary>
    /// <param name="level">A <see cref="LogLevel"/> as an integer.</param>
    /// <param name="channel">Channel tag; empty falls back to <see cref="DefaultChannel"/>.</param>
    /// <param name="message">The log message.</param>
    /// <param name="context">Structured data, or an empty dictionary.</param>
    /// <param name="file">Calling script path, from <c>get_stack()</c>.</param>
    /// <param name="member">Calling function name, from <c>get_stack()</c>.</param>
    /// <param name="line">Calling line number, from <c>get_stack()</c>.</param>
    public void GdWrite(int level, string channel, string message,
        Godot.Collections.Dictionary context, string file, string member, int line)
    {
        if (!Enum.IsDefined(typeof(LogLevel), level)) return;

        var logLevel = (LogLevel)level;
        string resolvedChannel = string.IsNullOrEmpty(channel) ? DefaultChannel : channel;

        // Gate before converting the dictionary, so a muted channel costs nothing beyond
        // this comparison even if a caller reached this method without checking first.
        if (!IsLevelEnabled(logLevel, resolvedChannel)) return;

        Write(logLevel,
            resolvedChannel,
            message ?? string.Empty,
            VariantConverter.ToContext(context),
            null,
            file ?? string.Empty,
            member ?? string.Empty,
            line);
    }

    /// <summary>GDScript entry point for the level gate.</summary>
    public bool GdIsEnabled(int level, string channel)
    {
        if (!Enum.IsDefined(typeof(LogLevel), level)) return false;

        return IsLevelEnabled((LogLevel)level, string.IsNullOrEmpty(channel) ? DefaultChannel : channel);
    }

    /// <summary>GDScript entry point: the current minimum level, as an integer.</summary>
    public int GdGetMinLevel() => (int)_minLogLevel;

    /// <summary>GDScript entry point: sets the minimum level from an integer.</summary>
    public void GdSetMinLevel(int level)
    {
        if (Enum.IsDefined(typeof(LogLevel), level)) _minLogLevel = (LogLevel)level;
    }

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
