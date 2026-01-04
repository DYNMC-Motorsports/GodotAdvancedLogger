using Godot;
using System;

public class ContextLogger
{
    public string ChannelName { get; private set; }

    public ContextLogger(string channelName)
    {
        ChannelName = channelName;
    }
    
    public void Info(string message) => LogManager.Info(ChannelName, message);
    public void Warning(string message) => LogManager.Warning(ChannelName, message);
    public void Error(string message, Exception ex = null) => LogManager.Error(ChannelName, message, ex);
    public void Debug(string message) => LogManager.Debug(ChannelName, message);
}
