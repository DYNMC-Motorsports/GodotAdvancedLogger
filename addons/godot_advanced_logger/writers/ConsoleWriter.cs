using Godot;
using System.Text;
using System.Linq;

public class ConsoleWriter : ILogWriter
{
    public void Initialize() { }
    public void Shutdown() { }

    public void Write(in LogEntry entry)
    {
        string color = GetColor(entry.Level);
        string time = entry.Timestamp.ToString("HH:mm:ss");
        
        var sb = new StringBuilder();
        sb.Append($"[color=gray]{time}[/color] [color={color}][b][{entry.Level}][/b][/color] [color=white][{entry.Channel}][/color] {entry.Message}");

        // Context-Daten schick in der Godot Konsole rendern
        if (entry.ContextData != null && entry.ContextData.Count > 0)
        {
            string contextStr = string.Join(" | ", entry.ContextData.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            sb.Append($" [color=gray][{contextStr}][/color]");
        }

        if (entry.Exception != null)
        {
            sb.Append($"\n[color=red]{entry.Exception}[/color]");
        }

        GD.PrintRich(sb.ToString());
    }

    private string GetColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Info => "green",
            LogLevel.Warning => "yellow",
            LogLevel.Error => "red",
            LogLevel.Debug => "cyan",
            _ => "white"
        };
    }
}