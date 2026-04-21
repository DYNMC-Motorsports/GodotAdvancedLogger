using System;
using System.Collections.Generic;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

public class ContextLogger(string channelName)
{
    public string ChannelName { get; private set; } = channelName;
    
    public bool IsEnabled(LogLevel level) => LogManager.IsLevelEnabled(level, ChannelName);

    public void Info(string message, Dictionary<string, object> context = null) 
        => LogManager.Log(LogLevel.Info, ChannelName, message, context);
        
    public void Warning(string message, Dictionary<string, object> context = null) 
        => LogManager.Log(LogLevel.Warning, ChannelName, message, context);
        
    public void Error(string message, Exception ex = null, Dictionary<string, object> context = null) 
        => LogManager.Log(LogLevel.Error, ChannelName, message, context, ex);
        
    public void Debug(string message, Dictionary<string, object> context = null) 
        => LogManager.Log(LogLevel.Debug, ChannelName, message, context);
}