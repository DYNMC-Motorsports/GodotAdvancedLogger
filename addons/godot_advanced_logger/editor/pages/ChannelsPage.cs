#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// Channel muting without typing comma-separated strings: muted channels as removable
/// chips, plus every channel found in the project's scripts and recent logs with a mute
/// switch next to it.
/// </summary>
internal sealed class ChannelsPage : SettingsPage
{
    public const string PageId = "channels";

    private const int MaxListedChannels = 300;

    public override string Id => PageId;
    public override string Title => "Channels";
    public override string Subtitle => "Silence noisy systems without touching code.";
    public override string IconName => "AudioMute";

    private HFlowContainer _chips;
    private LineEdit _addField;
    private Button _addButton;
    private Label _addError;
    private Label _storedPreview;
    private Button _unmuteAll;

    private LineEdit _filter;
    private VBoxContainer _knownList;
    private Label _scanSummary;
    private ChannelDiscovery.Result _scan;

    protected override void BuildContent(VBoxContainer root)
    {
        root.AddChild(Ui.Rich(
            "Every entry belongs to a channel, e.g. [code]Combat[/code], [code]AI[/code] or [code]Netcode[/code]. " +
            "Entries on a muted channel are dropped before they reach any output, at every level. " +
            "Matching ignores case, so [code]physics[/code] also mutes [code]Physics[/code]."));

        BuildMutedSection(root);
        BuildKnownSection(root);
        BuildRuntimeNote(root);
    }

    // ---------------------------------------------------------------------
    // Muted
    // ---------------------------------------------------------------------

    private void BuildMutedSection(VBoxContainer root)
    {
        var section = Ui.Section(root, "Muted channels");
        var card = Ui.Card(out var body);

        _chips = new HFlowContainer();
        _chips.AddThemeConstantOverride("h_separation", Ui.S(6));
        _chips.AddThemeConstantOverride("v_separation", Ui.S(6));
        body.AddChild(_chips);

        var addRow = Ui.HBox(8);
        _addField = new LineEdit
        {
            PlaceholderText = "Channel name, e.g. Physics (separate several with commas)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClearButtonEnabled = true,
        };
        _addField.TextSubmitted += _ => AddFromField();
        _addField.TextChanged += _ => ValidateField();
        addRow.AddChild(_addField);

        _addButton = new Button { Text = "Mute", Icon = Ui.Icon("Add"), Disabled = true };
        _addButton.Pressed += AddFromField;
        addRow.AddChild(_addButton);
        body.AddChild(addRow);

        _addError = Ui.Text("", subtle: true);
        _addError.Visible = false;
        _addError.AddThemeColorOverride("font_color", Ui.Warning);
        body.AddChild(_addError);

        var footer = Ui.HBox(8);
        footer.AddChild(Ui.Text("Stored as", subtle: true));
        _storedPreview = Ui.Mono("");
        _storedPreview.AddThemeColorOverride("font_color", Ui.Subtle);
        _storedPreview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _storedPreview.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        footer.AddChild(_storedPreview);

        _unmuteAll = new Button { Text = "Unmute all", Flat = true };
        _unmuteAll.Pressed += () => Store.SetString(LoggerSettings.MutedChannels, "");
        footer.AddChild(_unmuteAll);
        footer.AddChild(RevertButton(LoggerSettings.MutedChannels));
        body.AddChild(footer);

        section.AddChild(card);

        Bind(RefreshMuted);
    }

    private IReadOnlyList<string> Muted => LoggerSettings.ParseChannelList(Store.GetString(LoggerSettings.MutedChannels));

    private void RefreshMuted()
    {
        var muted = Muted;
        EditorUi.ClearChildren(_chips);

        if (muted.Count == 0)
        {
            _chips.AddChild(Ui.Text("No channel is muted. Everything that passes the level filter is logged.", subtle: true));
        }

        foreach (var channel in muted)
        {
            _chips.AddChild(BuildChip(channel));
        }

        string raw = Store.GetString(LoggerSettings.MutedChannels);
        _storedPreview.Text = string.IsNullOrEmpty(raw) ? "\"\"" : $"\"{raw}\"";
        _storedPreview.TooltipText = LoggerSettings.MutedChannels.Path;
        _unmuteAll.Visible = muted.Count > 1;
    }

    private Control BuildChip(string channel)
    {
        var chip = new PanelContainer();
        var style = Ui.Box(new Color(Ui.Warning, 0.14f), new Color(Ui.Warning, 0.5f), 12, 0);
        style.ContentMarginLeft = Ui.S(10);
        style.ContentMarginRight = Ui.S(4);
        style.ContentMarginTop = Ui.S(2);
        style.ContentMarginBottom = Ui.S(2);
        chip.AddThemeStyleboxOverride("panel", style);

        var row = Ui.HBox(4);
        row.AddChild(Ui.IconRect(Ui.Icon("AudioMute"), Ui.Warning));
        row.AddChild(Ui.Text(channel));

        var remove = Ui.IconButton("Close", $"Unmute {channel}");
        remove.Pressed += () => SetMuted(channel, false);
        row.AddChild(remove);

        chip.AddChild(row);
        return chip;
    }

    private void ValidateField()
    {
        var names = SplitInput(_addField.Text);
        var muted = new HashSet<string>(Muted, StringComparer.OrdinalIgnoreCase);
        var fresh = names.Where(n => !muted.Contains(n)).ToList();

        _addButton.Disabled = fresh.Count == 0;
        _addButton.Text = fresh.Count > 1 ? $"Mute {fresh.Count}" : "Mute";

        bool allDuplicates = names.Count > 0 && fresh.Count == 0;
        _addError.Visible = allDuplicates;
        _addError.Text = names.Count == 1 ? $"\"{names[0]}\" is already muted." : "These channels are already muted.";
    }

    private void AddFromField()
    {
        var names = SplitInput(_addField.Text);
        if (names.Count == 0) return;

        Store.SetString(LoggerSettings.MutedChannels, LoggerSettings.FormatChannelList(Muted.Concat(names)));
        _addField.Clear();
        ValidateField();
        _addField.GrabFocus();
    }

    private static List<string> SplitInput(string text) =>
        LoggerSettings.ParseChannelList(text).ToList();

    private void SetMuted(string channel, bool mute)
    {
        var muted = Muted.ToList();
        bool isMuted = muted.Any(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase));

        if (mute && !isMuted) muted.Add(channel);
        else if (!mute && isMuted) muted.RemoveAll(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase));
        else return;

        Store.SetString(LoggerSettings.MutedChannels, LoggerSettings.FormatChannelList(muted));
    }

    // ---------------------------------------------------------------------
    // Known channels
    // ---------------------------------------------------------------------

    private void BuildKnownSection(VBoxContainer root)
    {
        var section = Ui.Section(root, "Channels in this project",
            "Found by scanning your scripts for [code]new ContextLogger(\"…\")[/code], [code]channel: \"…\"[/code] " +
            "and [code]GodotLogger.info(…, \"…\")[/code], and by reading the most recent File and JSON Lines logs. " +
            "Channels built at runtime only show up once they have been logged to a file.");

        var toolbar = Ui.HBox(8);
        _filter = new LineEdit
        {
            PlaceholderText = "Filter channels",
            RightIcon = Ui.Icon("Search"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClearButtonEnabled = true,
        };
        _filter.TextChanged += _ => RefreshKnown();
        toolbar.AddChild(_filter);

        var rescan = new Button { Text = "Rescan", Icon = Ui.Icon("Reload") };
        rescan.Pressed += Rescan;
        toolbar.AddChild(rescan);
        section.AddChild(toolbar);

        _scanSummary = Ui.Text("", subtle: true);
        section.AddChild(_scanSummary);

        var card = Ui.Card(out var body);
        _knownList = Ui.VBox(0);
        body.AddChild(_knownList);
        section.AddChild(card);

        Bind(RefreshKnown);
    }

    /// <summary>Rescans on every visit (cheap, and picks up logs from the last run), but not on rapid tab flips.</summary>
    public override void OnShown()
    {
        if (_scan == null || (DateTime.Now - _scan.ScannedAt).TotalSeconds > 3) Rescan();
    }

    private void Rescan()
    {
        _scan = ChannelDiscovery.Scan();
        RefreshKnown();
    }

    private void RefreshKnown()
    {
        if (_knownList == null) return;
        EditorUi.ClearChildren(_knownList);

        if (_scan == null)
        {
            _scanSummary.Text = "";
            return;
        }

        var muted = new HashSet<string>(Muted, StringComparer.OrdinalIgnoreCase);

        // Muted channels that were not found anywhere still belong in the list, so they can be unmuted here too.
        var all = _scan.Channels.Values.ToList();
        foreach (var name in muted)
        {
            if (!_scan.Channels.ContainsKey(name))
                all.Add(new ChannelDiscovery.ChannelInfo { Name = name });
        }

        string filter = _filter.Text.Trim();
        var visible = all
            .Where(c => filter.Length == 0 || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.LogEntries)
            .ThenByDescending(c => c.CodeReferences)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListedChannels)
            .ToList();

        _scanSummary.Text =
            $"{all.Count} channel(s) · {_scan.SourceFilesScanned} script(s) and {_scan.LogFilesScanned} log file(s) scanned at {_scan.ScannedAt:T}";

        if (visible.Count == 0)
        {
            _knownList.AddChild(Ui.Text(filter.Length > 0 ? "No channel matches the filter." : "No channels found.", subtle: true));
            return;
        }

        for (int i = 0; i < visible.Count; i++)
        {
            if (i > 0) _knownList.AddChild(new HSeparator { Modulate = new Color(1, 1, 1, 0.4f) });
            _knownList.AddChild(BuildChannelRow(visible[i], muted.Contains(visible[i].Name)));
        }
    }

    private Control BuildChannelRow(ChannelDiscovery.ChannelInfo channel, bool isMuted)
    {
        var row = Ui.HBox(10);
        row.CustomMinimumSize = new Vector2(0, Ui.S(34));

        row.AddChild(Ui.IconRect(Ui.Icon(isMuted ? "AudioMute" : "AudioStreamPlayer") ?? Ui.Icon("Node"),
            isMuted ? Ui.Warning : Ui.Subtle));

        var name = Ui.Text(channel.Name);
        if (isMuted) name.AddThemeColorOverride("font_color", Ui.Subtle);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.AddChild(name);

        if (channel.IsDefault)
        {
            row.AddChild(Ui.Badge("default", Ui.Subtle, "Used when no channel is given"));
        }

        if (channel.CodeReferences > 0)
        {
            row.AddChild(Ui.Badge($"code · {channel.CodeReferences}", Ui.Accent,
                "Referenced in:\n" + string.Join("\n", channel.CodeLocations) +
                (channel.CodeReferences > channel.CodeLocations.Count ? "\n…" : "")));
        }

        if (channel.LogEntries > 0)
        {
            row.AddChild(Ui.Badge($"logs · {FormatCount(channel.LogEntries)}", Ui.Success,
                $"{channel.LogEntries} entries in the most recent log files"));
        }

        var toggle = new CheckButton
        {
            Text = isMuted ? "Muted" : "",
            ButtonPressed = isMuted,
            TooltipText = isMuted ? $"Unmute {channel.Name}" : $"Mute {channel.Name}",
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(Ui.S(90), 0),
            Alignment = HorizontalAlignment.Right,
        };
        string channelName = channel.Name;
        toggle.Toggled += on => SetMuted(channelName, on);
        row.AddChild(toggle);

        return row;
    }

    private static string FormatCount(int count) => count >= 10_000 ? $"{count / 1000}k" : count.ToString();

    private void BuildRuntimeNote(VBoxContainer root)
    {
        var card = Ui.Card(out var body, Ui.Accent);
        body.AddChild(Ui.Heading("Muting while the game runs", 1.0f));
        body.AddChild(Ui.Rich(
            "The list above is applied at startup. At runtime, type [code]mute Physics[/code], [code]unmute Physics[/code] " +
            "or [code]channels[/code] into the in-game console, or call [code]LogManager.Instance.MuteChannel(\"Physics\")[/code] " +
            "from C# or [code]GodotLogger.mute_channel(\"Physics\")[/code] from GDScript.", subtle: true));
        root.AddChild(card);
    }
}
#endif
