using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

public class ContextLogger(string channelName)
{
    public string ChannelName { get; private set; } = channelName;
    
    public bool IsEnabled(LogLevel level) => LogManager.IsLevelEnabled(level, ChannelName);

    public void Info(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.DebugLog(LogLevel.Info, ChannelName, message, context, file, member, line, null);
        
    public void Warning(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.DebugLog(LogLevel.Warning, ChannelName, message, context, file, member, line, null);
        
    public void Error(string message, Exception ex = null, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.DebugLog(LogLevel.Error, ChannelName, message, context, file, member, line, ex);
        
    [Conditional("DEBUG")]
    public void Debug(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.DebugLog(LogLevel.Debug, ChannelName, message, context, file, member, line, null);
}