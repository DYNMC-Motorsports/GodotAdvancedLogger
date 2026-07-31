using Godot;
using System;
using System.Collections.Generic;
using System.Text;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// The built-in "Logs" panel: a live, colour-coded log view with per-level and
/// per-channel filters.
/// </summary>
public class LogsDebugPanel : IDebugPanel
{
    private RichTextLabel _logOutput;
    private VBoxContainer _channelTogglesContainer;
    private HBoxContainer _levelTogglesContainer;

    private readonly Dictionary<string, bool> _channelFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LogLevel, bool> _levelFilters = new();

    private struct UiLogEntry
    {
        public LogLevel Level;
        public string Channel;

        /// <summary>Ready-to-render BBCode; all user-supplied text in it is already escaped.</summary>
        public string FormattedText;
    }

    private readonly Queue<UiLogEntry> _logHistory = new();

    private const int MaxLogLines = 1000;
    private const int ChunkSizeToRemove = 100;

    public string PanelName => "Logs";

    public void OnPanelShow() { }
    public void OnPanelHide() { }
    public void OnPanelUpdate(float delta) { }

    public Control GetPanelUi()
    {
        var mainContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        var filtersHSplit = new HBoxContainer();
        mainContainer.AddChild(filtersHSplit);

        var levelVBox = new VBoxContainer();
        filtersHSplit.AddChild(levelVBox);
        levelVBox.AddChild(new Label { Text = "Log Levels:" });
        _levelTogglesContainer = new HBoxContainer();
        levelVBox.AddChild(_levelTogglesContainer);

        foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
        {
            AddLevelFilter(level, true);
        }

        filtersHSplit.AddChild(new VSeparator());

        var channelVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        filtersHSplit.AddChild(channelVBox);
        channelVBox.AddChild(new Label { Text = "Channels:" });

        var scrollContainer = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 100),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _channelTogglesContainer = new VBoxContainer();
        scrollContainer.AddChild(_channelTogglesContainer);
        channelVBox.AddChild(scrollContainer);

        mainContainer.AddChild(BuildToolbar());
        mainContainer.AddChild(new HSeparator());

        _logOutput = new RichTextLabel
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = true
        };

        var monoFont = new SystemFont { FontNames = ["Consolas", "Courier New", "Monospace"] };
        _logOutput.AddThemeFontOverride("normal_font", monoFont);
        _logOutput.AddThemeFontOverride("bold_font", monoFont);

        mainContainer.AddChild(_logOutput);

        return mainContainer;
    }

    private Control BuildToolbar()
    {
        var toolbar = new HBoxContainer();

        var clearButton = new Button { Text = "Clear" };
        clearButton.Pressed += ClearLogs;
        toolbar.AddChild(clearButton);

        var copyButton = new Button { Text = "Copy visible" };
        copyButton.Pressed += CopyVisibleToClipboard;
        toolbar.AddChild(copyButton);

        return toolbar;
    }

    /// <summary>
    /// Copies the currently visible (filtered) log text to the system clipboard as plain
    /// text - handy for pasting into a bug report.
    /// </summary>
    public void CopyVisibleToClipboard()
    {
        if (_logOutput == null) return;

        // GetParsedText returns the rendered text with BBCode tags removed.
        DisplayServer.ClipboardSet(_logOutput.GetParsedText());
    }

    private void AddLevelFilter(LogLevel level, bool defaultEnabled)
    {
        _levelFilters[level] = defaultEnabled;
        var checkBox = new CheckBox { Text = level.ToString(), ButtonPressed = defaultEnabled };
        checkBox.Toggled += (pressed) =>
        {
            _levelFilters[level] = pressed;
            RefreshLogDisplay();
        };
        _levelTogglesContainer.AddChild(checkBox);
    }

    public void ProcessLog(LogEntry entry)
    {
        if (_logOutput == null) return;

        if (!_channelFilters.ContainsKey(entry.Channel))
        {
            AddChannelFilter(entry.Channel, true);
        }

        string formattedLog = Format(entry);

        _logHistory.Enqueue(new UiLogEntry
        {
            Level = entry.Level,
            Channel = entry.Channel,
            FormattedText = formattedLog
        });

        if (_logHistory.Count > MaxLogLines)
        {
            for (int i = 0; i < ChunkSizeToRemove; i++) _logHistory.Dequeue();
            RefreshLogDisplay();
        }
        else
        {
            if (_channelFilters[entry.Channel] && _levelFilters[entry.Level])
            {
                _logOutput.AppendText(formattedLog + "\n");
            }
        }
    }

    /// <summary>
    /// Renders an entry as BBCode. Every value that originates from the caller
    /// (message, channel, context, exception) is escaped: an unescaped '[' would be
    /// parsed as a tag by the RichTextLabel and silently swallow the rest of the line.
    /// </summary>
    private static string Format(in LogEntry entry)
    {
        string colorCode = entry.Level switch
        {
            LogLevel.Debug => "cyan",
            LogLevel.Info => "white",
            LogLevel.Warning => "yellow",
            LogLevel.Error => "red",
            _ => "white"
        };

        string timeStr = entry.Timestamp.ToString("HH:mm:ss");
        string lvlStr = entry.Level.ToString().PadRight(7);
        string chanStr = Bbcode.Escape(entry.Channel).PadRight(15);

        var sb = new StringBuilder();
        sb.Append("[color=gray][").Append(timeStr).Append("][/color] ")
          .Append("[color=").Append(colorCode).Append("][").Append(lvlStr).Append("][/color] ")
          .Append("[color=lightgray][").Append(chanStr).Append("][/color] ")
          .Append(Bbcode.Escape(entry.Message));

        if (entry.ContextData is { Count: > 0 })
        {
            sb.Append(" [color=gray]");
            bool first = true;

            foreach (var kvp in entry.ContextData)
            {
                sb.Append(first ? "" : ", ")
                  .Append(Bbcode.Escape(kvp.Key))
                  .Append('=')
                  .Append(Bbcode.Escape(kvp.Value?.ToString() ?? "null"));
                first = false;
            }

            sb.Append("[/color]");
        }

        if (entry.Exception != null)
        {
            sb.Append("\n[color=red]").Append(Bbcode.Escape(entry.Exception.ToString())).Append("[/color]");
        }

        return sb.ToString();
    }

    private void AddChannelFilter(string channel, bool defaultEnabled)
    {
        _channelFilters[channel] = defaultEnabled;
        var checkBox = new CheckBox { Text = channel, ButtonPressed = defaultEnabled };
        checkBox.Toggled += (pressed) =>
        {
            _channelFilters[channel] = pressed;
            RefreshLogDisplay();
        };
        _channelTogglesContainer.AddChild(checkBox);
    }

    private void RefreshLogDisplay()
    {
        if (_logOutput == null) return;
        _logOutput.Clear();

        var sb = new StringBuilder();
        foreach (var entry in _logHistory)
        {
            if (_levelFilters.TryGetValue(entry.Level, out bool levelEnabled) && levelEnabled &&
                _channelFilters.TryGetValue(entry.Channel, out bool channelEnabled) && channelEnabled)
            {
                sb.AppendLine(entry.FormattedText);
            }
        }
        _logOutput.AppendText(sb.ToString());
    }

    public void ClearLogs()
    {
        _logHistory.Clear();
        _logOutput?.Clear();
    }
}
