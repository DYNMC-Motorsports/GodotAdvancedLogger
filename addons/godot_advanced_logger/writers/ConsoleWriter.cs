using Godot;
using System;

public class ConsoleWriter : ILogWriter
{
    public void Initialize() { }
    public void Shutdown() { }

    public void Write(LogEntry entry)
    {
        string color = GetColor(entry.Level);
        string time = entry.Timestamp.ToString("HH:mm:ss");
        
        string logMessage = $"[color=gray]{time}[/color] [color={color}][b][{entry.Level}][/b][/color] [color=white][{entry.Channel}][/color] {entry.Message}";

        if (entry.Exception != null)
        {
            logMessage += $"\n[color=red]{entry.Exception}[/color]";
        }

        GD.PrintRich(logMessage);
    }

    private string GetColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Info => "green",    // Oder "white"
            LogLevel.Warning => "yellow",
            LogLevel.Error => "red",
            LogLevel.Debug => "cyan",
            _ => "white"
        };
    }
}
