using System.IO;
using System.Text;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

/// <summary>
/// Writes log entries to the Godot output console using rich text colors.
/// </summary>
/// <remarks>
/// Warnings and errors are additionally pushed through <c>GD.PushWarning</c> /
/// <c>GD.PushError</c> so they appear in the editor's <b>Debugger -&gt; Errors</b> tab
/// with a Godot stack trace, instead of scrolling past unnoticed in Output.
/// </remarks>
public class ConsoleWriter : ILogWriter
{
    private readonly bool _useGodotErrorStream;

    /// <param name="useGodotErrorStream">
    /// When <c>true</c> (default), <see cref="core.LogLevel.Warning"/> and
    /// <see cref="core.LogLevel.Error"/> entries are also reported to Godot's
    /// error stream. Set to <c>false</c> if you find the Debugger panel noisy.
    /// </param>
    public ConsoleWriter(bool useGodotErrorStream = true)
    {
        _useGodotErrorStream = useGodotErrorStream;
    }

    public void Initialize() { }
    public void Shutdown() { }

    public void Write(in core.LogEntry entry)
    {
        string color = GetColor(entry.Level);
        string time = entry.Timestamp.ToString("HH:mm:ss");
        string callerInfo = FormatCaller(entry);

        // Everything interpolated below that originates from user data is escaped:
        var sb = new StringBuilder();
        sb.Append("[color=gray]").Append(time).Append("[/color] ")
          .Append("[color=").Append(color).Append("][b][").Append(entry.Level).Append("][/b][/color] ")
          .Append("[color=lightgray](").Append(core.Bbcode.Escape(callerInfo)).Append(")[/color] ")
          .Append("[color=white][").Append(core.Bbcode.Escape(entry.Channel)).Append("][/color] ")
          .Append(core.Bbcode.Escape(entry.Message));

        if (entry.ContextData is { Count: > 0 })
        {
            sb.Append(" [color=gray][lb]").Append(core.Bbcode.Escape(FormatContext(entry))).Append("][/color]");
        }

        if (entry.Exception != null)
        {
            sb.Append("\n[color=red]").Append(core.Bbcode.Escape(entry.Exception.ToString())).Append("[/color]");
        }

        GD.PrintRich(sb.ToString());

        if (!_useGodotErrorStream) return;

        switch (entry.Level)
        {
            case core.LogLevel.Error:
                GD.PushError(FormatPlain(entry, callerInfo));
                break;
            case core.LogLevel.Warning:
                GD.PushWarning(FormatPlain(entry, callerInfo));
                break;
        }
    }

    /// <summary>
    /// Builds the plain, BBCode-free rendering handed to Godot's error stream.
    /// </summary>
    private static string FormatPlain(in core.LogEntry entry, string callerInfo)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(entry.Channel).Append("] ").Append(entry.Message);
        sb.Append(" (").Append(callerInfo).Append(')');

        if (entry.ContextData is { Count: > 0 })
        {
            sb.Append(" | ").Append(FormatContext(entry));
        }

        if (entry.Exception != null)
        {
            sb.Append('\n').Append(entry.Exception);
        }

        return sb.ToString();
    }

    private static string FormatContext(in core.LogEntry entry)
    {
        var sb = new StringBuilder();
        bool first = true;

        if (entry.ContextData == null) return sb.ToString();
        foreach (var kvp in entry.ContextData)
        {
            if (!first) sb.Append(" | ");
            sb.Append(kvp.Key).Append('=').Append(kvp.Value);
            first = false;
        }

        return sb.ToString();
    }

    private static string FormatCaller(in core.LogEntry entry)
    {
        string fileName = string.IsNullOrEmpty(entry.CallerFilePath)
            ? "Unknown"
            : Path.GetFileName(entry.CallerFilePath);

        return $"{fileName}:{entry.CallerLineNumber}";
    }

    private static string GetColor(core.LogLevel level)
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
