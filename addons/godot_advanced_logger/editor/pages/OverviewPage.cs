#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// Landing page: what the logger will actually do in each build type, plus every check
/// the <see cref="ConfigurationAnalyzer"/> raised, with one-click fixes.
/// </summary>
internal sealed class OverviewPage : SettingsPage
{
    public const string PageId = "overview";

    public override string Id => PageId;
    public override string Title => "Overview";
    public override string Subtitle => "What the logger does in each kind of build, and anything that needs your attention.";
    public override string IconName => "NodeInfo";

    private PanelContainer _statusCard;
    private TextureRect _statusIcon;
    private Label _statusTitle;
    private RichTextLabel _statusText;

    private GridContainer _matrix;
    private VBoxContainer _checks;

    protected override void BuildContent(VBoxContainer root)
    {
        // Status headline
        _statusCard = Ui.Card(out var statusBody);
        var statusRow = Ui.HBox(14);
        _statusIcon = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            CustomMinimumSize = new Vector2(Ui.S(24), Ui.S(24)),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        statusRow.AddChild(_statusIcon);

        var statusText = Ui.VBox(2);
        statusText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _statusTitle = Ui.Heading("", 1.15f);
        _statusText = Ui.Rich("", subtle: true);
        statusText.AddChild(_statusTitle);
        statusText.AddChild(_statusText);
        statusRow.AddChild(statusText);
        statusBody.AddChild(statusRow);
        root.AddChild(_statusCard);

        // Build matrix
        var matrixSection = Ui.Section(root, "Editor & debug builds vs. release exports",
            "[b]Editor & debug builds[/b] are runs started from the editor and exports with " +
            "[i]Export With Debug[/i] checked ([code]OS.IsDebugBuild()[/code] is true). " +
            "[b]Release exports[/b] are everything else. Developer tools are switched off in release on purpose.");

        var matrixCard = Ui.Card(out var matrixBody);
        _matrix = new GridContainer { Columns = 3 };
        _matrix.AddThemeConstantOverride("h_separation", Ui.S(24));
        _matrix.AddThemeConstantOverride("v_separation", Ui.S(10));
        matrixBody.AddChild(_matrix);
        matrixSection.AddChild(matrixCard);

        // Checks
        var checksSection = Ui.Section(root, "Checks");
        _checks = Ui.VBox(8);
        checksSection.AddChild(_checks);

        // Quick actions
        var actionsSection = Ui.Section(root, "Quick actions");
        var actions = new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation", Ui.S(8));
        actions.AddThemeConstantOverride("v_separation", Ui.S(8));

        var openLogs = Ui.IconButton("Folder", "Open the folder the File and JSON Lines outputs write to", "Open log folder");
        openLogs.Pressed += OutputsPage.OpenLogFolder;
        actions.AddChild(openLogs);

        var testSeq = Ui.IconButton("ToolConnect", "Send a test event to the configured Seq server", "Test Seq connection");
        testSeq.Pressed += () =>
        {
            Window.Navigate(SeqPage.PageId);
            Window.GetPage<SeqPage>()?.StartTest();
        };
        actions.AddChild(testSeq);

        var channels = Ui.IconButton("AudioMute", "Find and mute noisy channels", "Mute channels");
        channels.Pressed += () => Window.Navigate(ChannelsPage.PageId);
        actions.AddChild(channels);

        var docs = Ui.IconButton("ExternalLink", EditorUi.DocsUrl, "Read the docs");
        docs.Pressed += () => OS.ShellOpen(EditorUi.DocsUrl);
        actions.AddChild(docs);

        actionsSection.AddChild(actions);
    }

    protected override void OnRefresh()
    {
        if (Window == null) return;

        RefreshStatus();
        RefreshMatrix();
        RefreshChecks();
    }

    private void RefreshStatus()
    {
        var issues = Window.Issues;
        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
        int notes = issues.Count(i => i.Severity == IssueSeverity.Info);

        Color color;
        string iconName;

        if (errors > 0)
        {
            color = Ui.Error;
            iconName = "StatusError";
            _statusTitle.Text = errors == 1 ? "1 problem needs fixing" : $"{errors} problems need fixing";
        }
        else if (warnings > 0)
        {
            color = Ui.Warning;
            iconName = "StatusWarning";
            _statusTitle.Text = warnings == 1 ? "Looks good, with 1 warning" : $"Looks good, with {warnings} warnings";
        }
        else
        {
            color = Ui.Success;
            iconName = "StatusSuccess";
            _statusTitle.Text = "Everything looks good";
        }

        _statusIcon.Texture = Ui.Icon(iconName);
        _statusIcon.Modulate = Colors.White;

        var style = Ui.Box(Ui.CardBackground.Lerp(color, 0.08f), new Color(color, 0.5f), 6, 14);
        style.BorderWidthLeft = Ui.S(3);
        style.BorderColor = color;
        _statusCard.AddThemeStyleboxOverride("panel", style);

        string summary = $"{ActiveOutputCount(debug: true)} output(s) active while developing, " +
                         $"{ActiveOutputCount(debug: false)} in release exports.";
        if (notes > 0) summary += $" {notes} suggestion(s) below.";
        Ui.SetRichText(_statusText, summary);
    }

    private int ActiveOutputCount(bool debug)
    {
        int count = 0;
        if (Store.GetBool(LoggerSettings.EnableConsole)) count++;
        if (Store.GetBool(LoggerSettings.EnableFile)) count++;
        if (Store.GetBool(LoggerSettings.EnableJson)) count++;
        if (debug && Store.GetBool(LoggerSettings.EnableSeq)) count++;
        if (Store.GetBool(LoggerSettings.EnableOverlay)
            && (debug || Store.GetBool(LoggerSettings.OverlayAllowInRelease))) count++;
        return count;
    }

    private void RefreshMatrix()
    {
        EditorUi.ClearChildren(_matrix);

        AddMatrixHeader("");
        AddMatrixHeader("Editor & debug builds");
        AddMatrixHeader("Release exports");

        var debugLevel = (LogLevel)Store.GetInt(LoggerSettings.MinLogLevel);
        var releaseLevel = (LogLevel)Store.GetInt(LoggerSettings.MinLogLevelRelease);

        AddMatrixLabel("Minimum level", LevelsPage.PageId);
        AddLevelCell(debugLevel);
        AddLevelCell(releaseLevel == LogLevel.Debug ? LogLevel.Info : releaseLevel,
            releaseLevel == LogLevel.Debug ? "Set to Debug, which behaves like Info in release" : null);

        bool overlayRelease = Store.GetBool(LoggerSettings.OverlayAllowInRelease);

        AddOutputRow(LoggerSettings.EnableConsole, OutputsPage.PageId, true, true);
        AddOutputRow(LoggerSettings.EnableFile, OutputsPage.PageId, true, true);
        AddOutputRow(LoggerSettings.EnableJson, OutputsPage.PageId, true, true);
        AddOutputRow(LoggerSettings.EnableSeq, SeqPage.PageId, true, false, "Seq is debug-only");
        AddOutputRow(LoggerSettings.EnableOverlay, OverlayPage.PageId, true, overlayRelease,
            "Allow the overlay in release exports to change this");

        var muted = LoggerSettings.ParseChannelList(Store.GetString(LoggerSettings.MutedChannels));
        string mutedText = muted.Count == 0 ? "None" : string.Join(", ", muted.Take(4)) + (muted.Count > 4 ? $" +{muted.Count - 4}" : "");
        AddMatrixLabel("Muted channels", ChannelsPage.PageId);
        AddTextCell(mutedText, muted.Count > 0 ? Ui.Font : Ui.Subtle);
        AddTextCell(mutedText, muted.Count > 0 ? Ui.Font : Ui.Subtle);

        LoggerSettings.TryParseHotkey(Store.GetString(LoggerSettings.OverlayHotkey), out var key);
        string keyText = key == Key.None ? "F12 (fallback)" : OS.GetKeycodeString(key);
        AddMatrixLabel("Overlay hotkey", OverlayPage.PageId);
        AddTextCell(keyText, Ui.Font);
        AddTextCell(overlayRelease ? keyText : "Disabled", overlayRelease ? Ui.Font : Ui.Subtle);
    }

    private void AddOutputRow(SettingDefinition setting, string pageId, bool inDebug, bool inRelease, string releaseNote = null)
    {
        bool enabled = Store.GetBool(setting);
        AddMatrixLabel(setting.Label, pageId);
        AddStateCell(enabled && inDebug, null);
        AddStateCell(enabled && inRelease, enabled && !inRelease ? releaseNote : null);
    }

    private void AddMatrixHeader(string text)
    {
        var label = Ui.Text(text, subtle: true);
        label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(Ui.FontSize * 0.9f));
        _matrix.AddChild(label);
    }

    private void AddMatrixLabel(string text, string pageId)
    {
        var link = new LinkButton
        {
            Text = text,
            Underline = LinkButton.UnderlineMode.OnHover,
            TooltipText = "Open settings",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        link.AddThemeColorOverride("font_color", Ui.Font);
        link.Pressed += () => Window.Navigate(pageId);
        _matrix.AddChild(link);
    }

    private void AddStateCell(bool on, string reason)
    {
        var row = Ui.HBox(6);
        row.CustomMinimumSize = new Vector2(Ui.S(170), 0);
        row.AddChild(Ui.IconRect(Ui.Icon(on ? "StatusSuccess" : "Close"), on ? Colors.White : Ui.Subtle));
        var label = Ui.Text(on ? "On" : "Off", subtle: !on);
        row.AddChild(label);
        row.TooltipText = reason ?? string.Empty;
        if (!on && reason != null)
        {
            var hint = Ui.Text("· " + reason, subtle: true);
            hint.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(Ui.FontSize * 0.85f));
            row.AddChild(hint);
        }
        _matrix.AddChild(row);
    }

    private void AddLevelCell(LogLevel level, string tooltip = null)
    {
        var row = Ui.HBox(6);
        row.TooltipText = tooltip ?? string.Empty;
        row.AddChild(Ui.Badge(level + " +", Ui.LevelColor(level), tooltip));
        _matrix.AddChild(row);
    }

    private void AddTextCell(string text, Color color)
    {
        var label = Ui.Text(text);
        label.AddThemeColorOverride("font_color", color);
        _matrix.AddChild(label);
    }

    private void RefreshChecks()
    {
        EditorUi.ClearChildren(_checks);

        IReadOnlyList<ConfigIssue> issues = Window.Issues;

        if (issues.Count == 0)
        {
            var row = Ui.HBox(8);
            row.AddChild(Ui.IconRect(Ui.Icon("StatusSuccess")));
            row.AddChild(Ui.Text("No problems found.", subtle: true));
            _checks.AddChild(row);
            return;
        }

        foreach (var issue in issues)
        {
            _checks.AddChild(BuildIssueRow(Ui, Window, issue, showGoTo: true));
        }
    }

    /// <summary>Renders one finding as a card row. Shared with pages that show their own findings inline.</summary>
    internal static Control BuildIssueRow(EditorUi ui, LoggerSettingsWindow window, ConfigIssue issue, bool showGoTo)
    {
        var card = ui.Card(out var body, ui.SeverityColor(issue.Severity));
        var row = ui.HBox(12);
        row.AddChild(ui.IconRect(ui.SeverityIcon(issue.Severity)));

        var text = ui.VBox(2);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(ui.Heading(issue.Title, 1.0f));
        text.AddChild(ui.Rich(issue.Detail, subtle: true));
        row.AddChild(text);

        if (issue.Fix != null)
        {
            var fix = new Button { Text = issue.FixLabel ?? "Fix", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            var action = issue.Fix;
            fix.Pressed += () => action();
            row.AddChild(fix);
        }

        if (showGoTo)
        {
            var go = ui.IconButton("ArrowRight", "Go to the settings for this");
            string pageId = issue.PageId;
            go.Pressed += () => window.Navigate(pageId);
            row.AddChild(go);
        }

        body.AddChild(row);
        return card;
    }
}
#endif
