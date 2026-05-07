using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

public class ContextLogger(string channelName)
{
    public string ChannelName { get; private set; } = channelName;
    
    public bool IsEnabled(LogLevel level) => LogManager.IsLevelEnabled(level, ChannelName);

    public void Info(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.Log(LogLevel.Info, ChannelName, message, file, member, line, context, null);
        
    public void Warning(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.Log(LogLevel.Warning, ChannelName, message, file, member, line, context, null);
        
    public void Error(string message, Exception ex = null, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.Log(LogLevel.Error, ChannelName, message, file, member, line, context, ex);
        
    public void Debug(string message, Dictionary<string, object> context = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0) 
        => LogManager.Log(LogLevel.Debug, ChannelName, message, file, member, line, context, null);
}