using Godot;
using System;
using System.Collections.Generic;
using System.Text;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// The built-in "Logs" panel: a live, color-coded log view with substring search,
/// per-level and per-channel filters, and export to clipboard or file.
/// </summary>
public class LogsDebugPanel : IDebugPanel
{
    private const int MaxLogLines = 1000;
    private const int ChunkSizeToRemove = 100;

    private const string ExportDirectory = "user://logs/godotadvancedlogger";

    /// <summary>Pixels of slack below the scroll maximum still counted as "at the bottom".</summary>
    private const float ScrollBottomSlack = 24f;

    /// <summary>Consecutive off-bottom frames required before auto-follow switches itself off.</summary>
    private const int OffBottomFramesToUnfollow = 3;

    private RichTextLabel _logOutput;
    private VBoxContainer _channelTogglesContainer;
    private HBoxContainer _levelTogglesContainer;
    private LineEdit _searchBox;
    private CheckButton _followButton;
    private Label _statusLabel;

    private readonly Dictionary<string, bool> _channelFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LogLevel, bool> _levelFilters = new();

    /// <summary>Channel names in display order, kept sorted so the filter list is stable.</summary>
    private readonly List<string> _channelOrder = new();

    private string _searchTerm = string.Empty;
    private bool _follow = true;

    /// <summary>Entries currently passing the filters, tracked incrementally, see <see cref="UpdateStatus"/>.</summary>
    private int _visibleCount;

    private int _offBottomFrames;

    private struct UiLogEntry
    {
        public LogLevel Level;
        public string Channel;

        /// <summary>Ready-to-render BBCode; all user-supplied text in it is already escaped.</summary>
        public string FormattedText;

        /// <summary>Same line without markup; used for search, clipboard and file export.</summary>
        public string PlainText;
    }

    private readonly Queue<UiLogEntry> _logHistory = new();

    public string PanelName => "Logs";

    public void OnPanelShow() { }
    public void OnPanelHide() { }

    /// <remarks>
    /// If the user scrolls away from the bottom, auto-follow turns itself off so scrollback
    /// stays put while new entries keep arriving. The detection needs both a slack of a
    /// line or so and several consecutive frames off-bottom: appending text updates the
    /// scrollbar on the next draw, so a single frame below the maximum is normal and would
    /// otherwise disable following the moment a log arrived.
    /// </remarks>
    public void OnPanelUpdate(float delta)
    {
        if (!_follow || _logOutput == null || _followButton == null) return;

        var scroll = _logOutput.GetVScrollBar();
        if (scroll == null) return;

        bool atBottom = scroll.Value >= scroll.MaxValue - scroll.Page - ScrollBottomSlack;

        if (atBottom)
        {
            _offBottomFrames = 0;
            return;
        }

        _offBottomFrames++;
        if (_offBottomFrames < OffBottomFramesToUnfollow) return;

        _offBottomFrames = 0;
        _followButton.ButtonPressed = false;
    }

    public Control GetPanelUi()
    {
        var mainContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        mainContainer.AddChild(BuildToolbar());
        mainContainer.AddChild(BuildFilters());
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

        _statusLabel = new Label
        {
            Text = string.Empty,

            // The status line reports export paths, which are long enough to widen the
            // whole panel if the label were allowed to demand its text width.
            ClipText = true
        };
        mainContainer.AddChild(_statusLabel);

        return mainContainer;
    }

    private Control BuildToolbar()
    {
        var toolbar = new HBoxContainer();

        _searchBox = new LineEdit
        {
            PlaceholderText = "Search...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClearButtonEnabled = true
        };
        _searchBox.TextChanged += OnSearchChanged;
        toolbar.AddChild(_searchBox);

        _followButton = new CheckButton
        {
            Text = "Follow",
            ButtonPressed = true,
            TooltipText = "Keep scrolling to the newest entry"
        };
        _followButton.Toggled += OnFollowToggled;
        toolbar.AddChild(_followButton);

        var clearButton = new Button { Text = "Clear" };
        clearButton.Pressed += ClearLogs;
        toolbar.AddChild(clearButton);

        var copyButton = new Button { Text = "Copy", TooltipText = "Copy the visible entries to the clipboard" };
        copyButton.Pressed += CopyVisibleToClipboard;
        toolbar.AddChild(copyButton);

        var saveButton = new Button { Text = "Save", TooltipText = "Write the visible entries to a file in user://" };
        saveButton.Pressed += SaveVisibleToFile;
        toolbar.AddChild(saveButton);

        return toolbar;
    }

    private Control BuildFilters()
    {
        var filtersHSplit = new HBoxContainer();

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

        var channelHeader = new HBoxContainer();
        channelHeader.AddChild(new Label { Text = "Channels:" });
        channelHeader.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var allButton = new Button { Text = "All" };
        allButton.Pressed += () => SetAllChannels(true);
        channelHeader.AddChild(allButton);

        var noneButton = new Button { Text = "None" };
        noneButton.Pressed += () => SetAllChannels(false);
        channelHeader.AddChild(noneButton);

        channelVBox.AddChild(channelHeader);

        var scrollContainer = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 100),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _channelTogglesContainer = new VBoxContainer();
        scrollContainer.AddChild(_channelTogglesContainer);
        channelVBox.AddChild(scrollContainer);

        return filtersHSplit;
    }

    private void OnSearchChanged(string text)
    {
        _searchTerm = text ?? string.Empty;
        RefreshLogDisplay();
    }

    private void OnFollowToggled(bool pressed)
    {
        _follow = pressed;

        if (_logOutput == null) return;
        _logOutput.ScrollFollowing = pressed;

        if (pressed)
        {
            // Re-following should jump straight to the newest entry.
            var scroll = _logOutput.GetVScrollBar();
            if (scroll != null) scroll.Value = scroll.MaxValue;
        }
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
            AddChannel(entry.Channel);
        }

        var uiEntry = new UiLogEntry
        {
            Level = entry.Level,
            Channel = entry.Channel,
            FormattedText = FormatRich(entry),
            PlainText = FormatPlain(entry)
        };

        _logHistory.Enqueue(uiEntry);

        if (_logHistory.Count > MaxLogLines)
        {
            for (int i = 0; i < ChunkSizeToRemove; i++) _logHistory.Dequeue();
            RefreshLogDisplay();
        }
        else if (IsVisible(uiEntry))
        {
            _logOutput.AppendText(uiEntry.FormattedText + "\n");
            _visibleCount++;
            UpdateStatus();
        }
    }

    /// <summary>Whether an entry passes the current level, channel and search filters.</summary>
    private bool IsVisible(in UiLogEntry entry)
    {
        if (!_levelFilters.TryGetValue(entry.Level, out bool levelEnabled) || !levelEnabled) return false;
        if (!_channelFilters.TryGetValue(entry.Channel, out bool channelEnabled) || !channelEnabled) return false;

        if (_searchTerm.Length == 0) return true;

        return entry.PlainText.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renders an entry as BBCode. Every value that originates from the caller
    /// (message, channel, context, exception) is escaped: an unescaped '[' would be
    /// parsed as a tag by the RichTextLabel and silently swallow the rest of the line.
    /// </summary>
    private static string FormatRich(in LogEntry entry)
    {
        string colorCode = entry.Level switch
        {
            LogLevel.Debug => "cyan",
            LogLevel.Info => "white",
            LogLevel.Warning => "yellow",
            LogLevel.Error => "red",
            _ => "white"
        };

        var sb = new StringBuilder();
        sb.Append("[color=gray][").Append(entry.Timestamp.ToString("HH:mm:ss")).Append("][/color] ")
          .Append("[color=").Append(colorCode).Append("][").Append(entry.Level.ToString().PadRight(7)).Append("][/color] ")
          .Append("[color=lightgray][").Append(Bbcode.Escape(entry.Channel).PadRight(15)).Append("][/color] ")
          .Append(Bbcode.Escape(entry.Message));

        if (entry.ContextData is { Count: > 0 })
        {
            sb.Append(" [color=gray]");
            AppendContext(sb, entry, escape: true);
            sb.Append("[/color]");
        }

        if (entry.Exception != null)
        {
            sb.Append("\n[color=red]").Append(Bbcode.Escape(entry.Exception.ToString())).Append("[/color]");
        }

        return sb.ToString();
    }

    /// <summary>Markup-free rendering used for search, clipboard and file export.</summary>
    private static string FormatPlain(in LogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(entry.Timestamp.ToString("HH:mm:ss")).Append("] ")
          .Append('[').Append(entry.Level.ToString().PadRight(7)).Append("] ")
          .Append('[').Append(entry.Channel.PadRight(15)).Append("] ")
          .Append(entry.Message);

        if (entry.ContextData is { Count: > 0 })
        {
            sb.Append(' ');
            AppendContext(sb, entry, escape: false);
        }

        if (entry.Exception != null)
        {
            sb.Append('\n').Append(entry.Exception);
        }

        return sb.ToString();
    }

    private static void AppendContext(StringBuilder sb, in LogEntry entry, bool escape)
    {
        bool first = true;

        foreach (var kvp in entry.ContextData)
        {
            if (!first) sb.Append(", ");

            string key = escape ? Bbcode.Escape(kvp.Key) : kvp.Key;
            string value = kvp.Value?.ToString() ?? "null";
            if (escape) value = Bbcode.Escape(value);

            sb.Append(key).Append('=').Append(value);
            first = false;
        }
    }

    /// <summary>Adds a newly seen channel and rebuilds the filter list alphabetically.</summary>
    private void AddChannel(string channel)
    {
        _channelFilters[channel] = true;

        int index = _channelOrder.BinarySearch(channel, StringComparer.OrdinalIgnoreCase);
        if (index < 0) _channelOrder.Insert(~index, channel);

        RebuildChannelToggles();
    }

    private void RebuildChannelToggles()
    {
        if (_channelTogglesContainer == null) return;

        foreach (var child in _channelTogglesContainer.GetChildren())
        {
            _channelTogglesContainer.RemoveChild(child);
            child.QueueFree();
        }

        foreach (string channel in _channelOrder)
        {
            string captured = channel;

            var checkBox = new CheckBox
            {
                Text = captured,
                ButtonPressed = _channelFilters.TryGetValue(captured, out bool on) && on
            };

            checkBox.Toggled += (pressed) =>
            {
                _channelFilters[captured] = pressed;
                RefreshLogDisplay();
            };

            _channelTogglesContainer.AddChild(checkBox);
        }
    }

    private void SetAllChannels(bool enabled)
    {
        foreach (string channel in _channelOrder)
        {
            _channelFilters[channel] = enabled;
        }

        RebuildChannelToggles();
        RefreshLogDisplay();
    }

    private void RefreshLogDisplay()
    {
        if (_logOutput == null) return;
        _logOutput.Clear();

        var sb = new StringBuilder();
        int visible = 0;

        foreach (var entry in _logHistory)
        {
            if (!IsVisible(entry)) continue;

            sb.AppendLine(entry.FormattedText);
            visible++;
        }

        _visibleCount = visible;
        _logOutput.AppendText(sb.ToString());
        UpdateStatus();
    }

    /// <remarks>
    /// The visible count is tracked incrementally rather than recomputed here: this runs
    /// once per appended entry, and rescanning the full 1000-entry history each time would
    /// cost hundreds of thousands of comparisons per frame during a log burst.
    /// </remarks>
    private void UpdateStatus()
    {
        if (_statusLabel == null) return;
        _statusLabel.Text = $"  {_visibleCount} shown / {_logHistory.Count} buffered (max {MaxLogLines})";
    }

    /// <summary>Builds the plain text of every currently visible entry.</summary>
    private string BuildVisibleText()
    {
        var sb = new StringBuilder();
        foreach (var entry in _logHistory)
        {
            if (IsVisible(entry)) sb.AppendLine(entry.PlainText);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Copies the currently visible (filtered) entries to the system clipboard as plain
    /// text
    /// </summary>
    public void CopyVisibleToClipboard()
    {
        DisplayServer.ClipboardSet(BuildVisibleText());
        SetStatus("Copied visible entries to the clipboard.");
    }

    /// <summary>
    /// Writes the currently visible entries to <c>user://logs/godotadvancedlogger</c> and
    /// reports the absolute path.
    /// </summary>
    public void SaveVisibleToFile()
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(ExportDirectory);

            string path = $"{ExportDirectory}/export_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";

            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                SetStatus($"Could not write export: {FileAccess.GetOpenError()}");
                return;
            }

            file.StoreString(BuildVisibleText());

            string absolute = ProjectSettings.GlobalizePath(path);
            SetStatus($"Saved to {absolute}");
            LogManager.Info("Exported debug log", new Dictionary<string, object> { { "Path", absolute } }, "DebugScreen");
        }
        catch (Exception e)
        {
            SetStatus($"Export failed: {e.Message}");
        }
    }

    private void SetStatus(string message)
    {
        if (_statusLabel != null) _statusLabel.Text = "  " + message;
    }

    /// <summary>Every channel seen this session, alphabetically.</summary>
    public List<string> GetKnownChannels() => new(_channelOrder);

    public void ClearLogs()
    {
        _logHistory.Clear();
        _visibleCount = 0;
        _logOutput?.Clear();
        UpdateStatus();
    }
}
