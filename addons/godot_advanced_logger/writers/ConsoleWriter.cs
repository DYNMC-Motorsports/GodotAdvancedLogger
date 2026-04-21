using System.Linq;
using System.Text;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

public class ConsoleWriter : ILogWriter
{
    public void Initialize() { }
    public void Shutdown() { }

    public void Write(in core.LogEntry entry)
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

    private string GetColor(core.LogLevel level)
    {
        return level switch
        {
            core.LogLevel.Info => "green",
            core.LogLevel.Warning => "yellow",
            core.LogLevel.Error => "red",
            core.LogLevel.Debug => "cyan",
            _ => "white"
        };
    }
}