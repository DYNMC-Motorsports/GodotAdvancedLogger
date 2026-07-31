using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

/// <summary>
/// A logger bound to a single channel. Create one per system (Combat, AI, Netcode, ...)
/// so every entry it produces is tagged consistently and can be muted or filtered as a group.
/// </summary>
/// <param name="channelName">Channel tag applied to every entry from this logger.</param>
public class ContextLogger(string channelName)
{
    public string ChannelName { get; } = channelName;

    /// <summary>
    /// Returns whether an entry at <paramref name="level"/> on this channel would be written.
    /// Use it to guard expensive context construction:
    /// <c>if (!log.IsEnabled(LogLevel.Info)) return;</c>
    /// </summary>
    public bool IsEnabled(LogLevel level) => LogManager.IsLevelEnabled(level, ChannelName);

    public void Info(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => LogManager.Write(LogLevel.Info, ChannelName, message, context, null, file, member, line);

    public void Warning(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => LogManager.Write(LogLevel.Warning, ChannelName, message, context, null, file, member, line);

    public void Error(string message, Exception ex = null, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => LogManager.Write(LogLevel.Error, ChannelName, message, context, ex, file, member, line);

    /// <summary>
    /// Logs at <see cref="LogLevel.Debug"/>. Stripped by the compiler when <c>DEBUG</c>
    /// is not defined, so the call and its arguments cost nothing in a release export.
    /// </summary>
    [Conditional("DEBUG")]
    public void Debug(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => LogManager.Write(LogLevel.Debug, ChannelName, message, context, null, file, member, line);
}
