#if TOOLS
using System;
using System.IO;
using System.Linq;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// One card per output ("writer"): what it does, in which builds it runs, and where its
/// data ends up.
/// </summary>
internal sealed class OutputsPage : SettingsPage
{
    public const string PageId = "outputs";

    public override string Id => PageId;
    public override string Title => "Outputs";
    public override string Subtitle => "Where log entries go. Any combination can run at the same time.";
    public override string IconName => "Signals";

    private Label _folderStats;
    private Button _openFolder;

    protected override void BuildContent(VBoxContainer root)
    {
        root.AddChild(Ui.Rich(
            "Each entry that passes the level and channel filters is handed to every enabled output. " +
            "Outputs write on background threads, so enabling several has little effect on frame time."));

        OutputCard(root, LoggerSettings.EnableConsole, "Terminal", AllBuilds(),
            "Prints entries to the editor's [b]Output[/b] panel and the terminal, colored by level. " +
            "Warnings and errors are also pushed to [b]Debugger → Errors[/b] with a stack trace, " +
            "so they do not scroll past unnoticed.");

        var overlayCard = OutputCard(root, LoggerSettings.EnableOverlay, "CanvasLayer", DebugOnlyBadge(allowOptIn: true),
            "Feeds the [b]Logs[/b] tab of the in-game debug overlay, with search and level/channel filters. " +
            "This switch only controls the Logs tab. The overlay itself (Console, Watch, Performance, System) " +
            "is set up on the [url=page:overlay]In-Game Overlay[/url] page.");
        overlayCard.AddChild(LinkRow("Overlay settings", OverlayPage.PageId));

        var fileCard = OutputCard(root, LoggerSettings.EnableFile, "File", AllBuilds(),
            "Writes one tab-separated [code].txt[/code] file per session, easy to read and to attach to bug reports. " +
            "The newest 50 files are kept; older ones are deleted at startup.");
        fileCard.AddChild(Example("2026-10-19 21:04:11.532\tWARNING\tCombat\tPlayer Damaged\t{\"Damage\":42}"));

        var jsonCard = OutputCard(root, LoggerSettings.EnableJson, "FileList", AllBuilds(),
            "Writes [url=https://jsonlines.org]JSON Lines[/url] ([code].jsonl[/code]): one self-contained JSON object per line, " +
            "ready for scripts, [code]jq[/code] or log shippers. The newest 50 files are kept.");
        jsonCard.AddChild(Example("{\"sessionId\":\"3fa2c1d0\",\"timestamp\":\"2026-10-19T21:04:11.532\",\"level\":\"Warning\"," +
                                  "\"channel\":\"Combat\",\"caller\":\"Player.cs:88 (TakeDamage)\",\"message\":\"Player Damaged\"," +
                                  "\"context\":{\"Damage\":42}}"));

        var seqCard = OutputCard(root, LoggerSettings.EnableSeq, "ToolConnect", DebugOnlyBadge(allowOptIn: false),
            "Streams entries to a [url=https://datalust.co/seq]Seq[/url] server in real time: a free local dashboard " +
            "where you can search structured data, e.g. [code]Channel = 'Combat' and Damage > 50[/code]. " +
            "Never active in release exports.");
        seqCard.AddChild(LinkRow("Configure Seq and test the connection", SeqPage.PageId));

        BuildFolderSection(root);
    }

    private VBoxContainer OutputCard(VBoxContainer root, SettingDefinition setting, string iconName, Control badge, string description)
    {
        var card = Ui.Card(out var body);
        var header = Ui.HBox(10);

        var icon = Ui.IconRect(Ui.Icon(iconName));
        header.AddChild(icon);
        header.AddChild(Ui.Heading(setting.Label, 1.08f));
        header.AddChild(badge);
        header.AddChild(EditorUi.Spacer());
        header.AddChild(RevertButton(setting));
        header.AddChild(Toggle(setting));
        body.AddChild(header);

        var description_ = Ui.Rich(description, subtle: true);
        body.AddChild(description_);

        // Dim the card while the output is off, so the page reads at a glance.
        Bind(() => card.Modulate = Store.GetBool(setting) ? Colors.White : new Color(1, 1, 1, 0.72f));

        root.AddChild(card);
        return body;
    }

    private Control AllBuilds() => Ui.Badge("All builds", Ui.Success, "Runs in the editor, debug exports and release exports.");

    private Control DebugOnlyBadge(bool allowOptIn) => Ui.Badge(
        allowOptIn ? "Debug builds · release opt-in" : "Debug builds only",
        Ui.Accent,
        allowOptIn
            ? "Runs in the editor and debug exports; in release only if the overlay is allowed in release."
            : "Runs in the editor and debug exports. Hard-disabled in release exports.");

    private Control LinkRow(string text, string pageId)
    {
        var link = new LinkButton { Text = text + "  ›", Underline = LinkButton.UnderlineMode.OnHover };
        link.AddThemeColorOverride("font_color", Ui.Accent);
        link.Pressed += () => Window.Navigate(pageId);
        return link;
    }

    private Control Example(string line)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0.18f), null, 4, 8));
        // Tabs render as nothing in a Label; show them as visible gaps.
        var label = Ui.Mono(line.Replace("\t", "   "));
        label.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.CustomMinimumSize = new Vector2(Ui.S(120), 0);
        label.AddThemeColorOverride("font_color", Ui.Subtle);
        label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(Ui.FontSize * 0.88f));
        panel.AddChild(label);
        return panel;
    }

    private void BuildFolderSection(VBoxContainer root)
    {
        var section = Ui.Section(root, "Log files",
            "The File and JSON Lines outputs share one folder inside the project's user data directory. " +
            "In the editor this is the same folder your game writes to when you run it.");

        var card = Ui.Card(out var body);
        var row = Ui.HBox(10);
        row.AddChild(Ui.IconRect(Ui.Icon("Folder")));

        var text = Ui.VBox(2);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var path = Ui.Mono(ProjectSettings.GlobalizePath(LoggerSettings.LogDirectory));
        path.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        path.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        path.CustomMinimumSize = new Vector2(Ui.S(120), 0);
        text.AddChild(path);
        _folderStats = Ui.Text("", subtle: true);
        text.AddChild(_folderStats);
        row.AddChild(text);

        var copy = Ui.IconButton("ActionCopy", "Copy path");
        copy.Pressed += () => DisplayServer.ClipboardSet(ProjectSettings.GlobalizePath(LoggerSettings.LogDirectory));
        row.AddChild(copy);

        _openFolder = new Button { Text = "Open folder", Icon = Ui.Icon("Folder"), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _openFolder.Pressed += OpenLogFolder;
        row.AddChild(_openFolder);

        body.AddChild(row);
        section.AddChild(card);
    }

    public override void OnShown() => RefreshFolderStats();

    private void RefreshFolderStats()
    {
        string dir = ProjectSettings.GlobalizePath(LoggerSettings.LogDirectory);

        try
        {
            if (!Directory.Exists(dir))
            {
                _folderStats.Text = "No log files yet. Enable a file output and run the game.";
                return;
            }

            var files = new DirectoryInfo(dir).GetFiles("log_*.*");
            int txt = files.Count(f => f.Extension == ".txt");
            int jsonl = files.Count(f => f.Extension == ".jsonl");
            long bytes = files.Sum(f => f.Length);
            var newest = files.OrderByDescending(f => f.LastWriteTime).FirstOrDefault();

            _folderStats.Text = files.Length == 0
                ? "The folder is empty."
                : $"{txt} text · {jsonl} JSON Lines · {FormatBytes(bytes)}" +
                  (newest != null ? $" · newest {newest.LastWriteTime:g}" : "");
        }
        catch (Exception e)
        {
            _folderStats.Text = "Could not read the folder: " + e.Message;
        }
    }

    /// <summary>Opens the log folder in the OS file manager, creating it if needed so the button always works.</summary>
    public static void OpenLogFolder()
    {
        string dir = ProjectSettings.GlobalizePath(LoggerSettings.LogDirectory);

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[GodotAdvancedLogger] Could not create {dir}: {e.Message}");
        }

        OS.ShellShowInFileManager(dir);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };
}
#endif
