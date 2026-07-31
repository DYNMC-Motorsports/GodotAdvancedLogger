using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Registry of commands runnable from the in-game Console panel.
/// </summary>
/// <remarks>
/// Deliberately static so gameplay code can register commands at any time without caring
/// whether the overlay exists yet, and without holding a reference to it.
/// </remarks>
/// <example>
/// <code>
/// DebugConsole.Register("give_gold", args =>
/// {
///     if (args.Length &lt; 1 || !int.TryParse(args[0], out int amount))
///         return "usage: give_gold &lt;amount&gt;";
///
///     Player.Gold += amount;
///     return $"Gave {amount} gold.";
/// }, "Adds gold to the player", "give_gold &lt;amount&gt;");
/// </code>
/// </example>
public static class DebugConsole
{
    /// <summary>A single registered command.</summary>
    public sealed class Command
    {
        public string Name { get; init; }
        public string Description { get; init; }
        public string Usage { get; init; }
        public Func<string[], string> Handler { get; init; }
    }

    private static readonly Dictionary<string, Command> Registry = new(StringComparer.OrdinalIgnoreCase);
    private static bool _builtInsRegistered;

    /// <summary>All registered commands, ordered by name.</summary>
    public static IEnumerable<Command> All => Registry.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a command. Names are case-insensitive; registering an existing name
    /// replaces the previous handler.
    /// </summary>
    /// <param name="name">Command word, without spaces.</param>
    /// <param name="handler">Receives the parsed arguments and returns text to print (may be null).</param>
    /// <param name="description">One-line summary shown by <c>help</c>.</param>
    /// <param name="usage">Argument syntax shown by <c>help &lt;name&gt;</c>.</param>
    public static void Register(string name, Func<string[], string> handler, string description = "", string usage = "")
    {
        if (string.IsNullOrWhiteSpace(name) || handler == null) return;

        name = name.Trim();

        Registry[name] = new Command
        {
            Name = name,
            Description = description ?? string.Empty,
            Usage = string.IsNullOrEmpty(usage) ? name : usage,
            Handler = handler
        };
    }

    /// <summary>Removes a command.</summary>
    public static bool Unregister(string name)
        => !string.IsNullOrEmpty(name) && Registry.Remove(name);

    public static bool TryGet(string name, out Command command)
        => Registry.TryGetValue(name ?? string.Empty, out command);

    /// <summary>Command names starting with <paramref name="prefix"/>, for tab completion.</summary>
    public static List<string> CompletionsFor(string prefix)
    {
        prefix ??= string.Empty;

        return Registry.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Parses and runs a command line, returning the text to print.
    /// Handler exceptions are caught and returned as text so a bad command cannot take
    /// the game down.
    /// </summary>
    public static string Execute(string line) => Execute(line, out _);

    /// <inheritdoc cref="Execute(string)"/>
    /// <param name="isError">
    /// <c>true</c> when the command was unknown or its handler threw, so a caller can
    /// render the result differently from ordinary output.
    /// </param>
    public static string Execute(string line, out bool isError)
    {
        isError = false;

        var tokens = Tokenize(line);
        if (tokens.Length == 0) return null;

        string name = tokens[0];

        if (!Registry.TryGetValue(name, out var command))
        {
            isError = true;

            var suggestions = CompletionsFor(name);
            return suggestions.Count > 0
                ? $"Unknown command '{name}'. Did you mean: {string.Join(", ", suggestions)}?"
                : $"Unknown command '{name}'. Type 'help' for a list.";
        }

        var args = new string[tokens.Length - 1];
        Array.Copy(tokens, 1, args, 0, args.Length);

        try
        {
            return command.Handler(args);
        }
        catch (Exception e)
        {
            isError = true;
            return $"Command '{name}' failed: {e.GetType().Name}: {e.Message}";
        }
    }

    /// <summary>
    /// Splits a command line into tokens, treating double-quoted runs as single arguments
    /// so <c>say "hello world"</c> passes one argument rather than two.
    /// </summary>
    public static string[] Tokenize(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return Array.Empty<string>();

        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) tokens.Add(current.ToString());

        return tokens.ToArray();
    }

    /// <summary>
    /// Registers the commands that ship with the plugin. Called once by
    /// <see cref="DebugScreen"/>; safe to call again.
    /// </summary>
    internal static void RegisterBuiltIns()
    {
        if (_builtInsRegistered) return;
        _builtInsRegistered = true;

        Register("help", HelpCommand,
            "Lists commands, or shows details for one", "help [command]");

        Register("quit", _ =>
        {
            DebugScreen.Instance?.GetTree()?.Quit();
            return "Quitting.";
        }, "Closes the game");

        Register("loglevel", LogLevelCommand,
            "Shows or sets the minimum log level", "loglevel [Debug|Info|Warning|Error]");

        Register("mute", args =>
        {
            if (args.Length < 1) return "usage: mute <channel>";
            LogManager.Instance?.MuteChannel(args[0]);
            return $"Muted channel '{args[0]}'.";
        }, "Stops a channel from being logged", "mute <channel>");

        Register("unmute", args =>
        {
            if (args.Length < 1) return "usage: unmute <channel>";
            LogManager.Instance?.UnmuteChannel(args[0]);
            return $"Unmuted channel '{args[0]}'.";
        }, "Re-enables a muted channel", "unmute <channel>");

        Register("channels", _ =>
        {
            var known = DebugScreen.Instance?.GetLogsPanel()?.GetKnownChannels();
            if (known == null || known.Count == 0) return "No channels seen yet.";

            var sb = new StringBuilder();
            foreach (string channel in known)
            {
                bool muted = LogManager.Instance?.IsChannelMuted(channel) ?? false;
                sb.Append("  ").Append(channel).AppendLine(muted ? "  (muted)" : string.Empty);
            }

            return sb.ToString().TrimEnd();
        }, "Lists every channel seen this session");

        Register("panels", _ =>
        {
            var screen = DebugScreen.Instance;
            if (screen == null) return "Debug screen unavailable.";

            var names = screen.GetPanelNames();
            return names.Count == 0 ? "No panels registered." : "  " + string.Join("\n  ", names);
        }, "Lists the registered debug panels");

        Register("panel", args =>
        {
            if (args.Length < 1) return "usage: panel <name>";

            var screen = DebugScreen.Instance;
            if (screen == null) return "Debug screen unavailable.";
            if (screen.GetPanel(args[0]) == null) return $"No panel named '{args[0]}'.";

            screen.ShowPanel(args[0]);
            return $"Switched to '{args[0]}'.";
        }, "Switches to a panel by name", "panel <name>");

        Register("logtest", _ =>
        {
            var logger = new ContextLogger("ConsoleTest");
            logger.Debug("Debug test message");
            logger.Info("Info test message", new Dictionary<string, object> { { "Source", "console" } });
            logger.Warning("Warning test message");
            logger.Error("Error test message", new InvalidOperationException("Synthetic test exception"));
            return "Emitted one entry at each level on channel 'ConsoleTest'.";
        }, "Emits one log entry at each level, to verify writers");
    }

    private static string HelpCommand(string[] args)
    {
        if (args.Length > 0)
        {
            if (!Registry.TryGetValue(args[0], out var command))
                return $"Unknown command '{args[0]}'.";

            var detail = new StringBuilder();
            detail.Append(command.Name);
            if (!string.IsNullOrEmpty(command.Description)) detail.Append(" - ").Append(command.Description);
            detail.AppendLine();
            detail.Append("  usage: ").Append(command.Usage);
            return detail.ToString();
        }

        var sb = new StringBuilder();
        sb.AppendLine("Available commands:");

        int width = 0;
        foreach (var command in Registry.Values)
        {
            if (command.Name.Length > width) width = command.Name.Length;
        }

        foreach (var command in All)
        {
            sb.Append("  ").Append(command.Name.PadRight(width + 2)).AppendLine(command.Description);
        }

        return sb.ToString().TrimEnd();
    }

    private static string LogLevelCommand(string[] args)
    {
        var manager = LogManager.Instance;
        if (manager == null) return "LogManager is not available.";

        if (args.Length == 0)
        {
            return $"Minimum log level is {manager.MinLogLevel}.";
        }

        if (!Enum.TryParse(args[0], true, out LogLevel level))
        {
            return $"'{args[0]}' is not a log level. Expected one of: {string.Join(", ", Enum.GetNames<LogLevel>())}.";
        }

        manager.MinLogLevel = level;
        return $"Minimum log level set to {level}.";
    }
}
