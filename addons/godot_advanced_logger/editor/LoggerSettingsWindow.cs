#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// The dedicated settings window: a sidebar with one page per area, a scrollable page
/// body and a footer. Opened from <b>Project → Tools → Advanced Logger Settings...</b>
/// or the command palette.
/// </summary>
[Tool]
public partial class LoggerSettingsWindow : Window
{
    private const string PluginConfigPath = "res://addons/godot_advanced_logger/plugin.cfg";

    private EditorUi _ui;
    private SettingsStore _store;
    private readonly List<SettingsPage> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly Dictionary<string, Label> _navBadges = new();
    private readonly Dictionary<string, Control> _pageRoots = new();

    private Label _pageTitle;
    private Label _pageSubtitle;
    private TextureRect _pageIcon;
    private ScrollContainer _scroll;
    private SettingsPage _current;
    private bool _refreshQueued;

    internal IReadOnlyList<ConfigIssue> Issues { get; private set; } = new List<ConfigIssue>();

    /// <summary>False after a C# assembly reload restored this node without its managed state.</summary>
    internal bool IsBuilt => _ui != null && _store != null;

    internal SettingsStore Store => _store;

    public LoggerSettingsWindow()
    {
        Title = "Godot Advanced Logger · Settings";
        Visible = false;
        Transient = true;
        Exclusive = false;
        WrapControls = false;
    }

    public override void _EnterTree()
    {
        if (IsBuilt) return;

        _ui = new EditorUi();
        _ui.NavigateRequested += Navigate;
        MinSize = new Vector2I(_ui.S(780), _ui.S(520));

        _store = new SettingsStore();
        _store.Changed += QueueRefresh;

        // Engine-held connections use object + method name, never lambdas: those survive a
        // C# assembly reload and are dropped automatically when the window is freed.
        ProjectSettings.Singleton.Connect(ProjectSettings.SignalName.SettingsChanged,
            new Callable(this, MethodName.OnProjectSettingsChanged));
        Connect(Window.SignalName.CloseRequested, new Callable(this, Window.MethodName.Hide));
        Connect(Window.SignalName.VisibilityChanged, new Callable(this, MethodName.OnVisibilityChanged));

        _pages.Add(new OverviewPage());
        _pages.Add(new LevelsPage());
        _pages.Add(new OutputsPage());
        _pages.Add(new SeqPage());
        _pages.Add(new ChannelsPage());
        _pages.Add(new OverlayPage());

        BuildLayout();
        RefreshNow();
        Navigate(OverviewPage.PageId);
    }

    public override void _ExitTree() => ReleaseManagedResources();

    public override void _Process(double delta)
    {
        if (!IsBuilt)
        {
            // A C# assembly reload (after every build) restores this node but not its managed
            // state, leaving a window whose controls point at unloaded code. Remove it; the
            // plugin builds a fresh one the next time the settings are opened.
            if (IsInsideTree())
            {
                Hide();
                QueueFree();
            }
            return;
        }

        _store.Tick();
    }

    /// <summary>Flushes pending saves. Called when the window leaves the tree and right before a C# assembly reload.</summary>
    internal void ReleaseManagedResources()
    {
        _store?.Flush();
    }

    private void OnProjectSettingsChanged() => _store?.NotifyExternalChange();

    private void OnVisibilityChanged()
    {
        if (!Visible) _store?.Flush();
    }

    /// <summary>Shows the window centered over the editor, or focuses it if already open.</summary>
    public void Open()
    {
        if (!IsBuilt) return;

        if (!Visible)
        {
            PopupCenteredClamped(new Vector2I(_ui.S(1040), _ui.S(720)), 0.9f);
        }
        else
        {
            GrabFocus();
        }

        RefreshNow();
        _current?.OnShown();
    }

    internal void Navigate(string pageId)
    {
        var page = _pages.FirstOrDefault(p => p.Id == pageId);
        if (page == null) return;

        _current = page;

        foreach (var (id, root) in _pageRoots) root.Visible = id == pageId;
        foreach (var (id, button) in _navButtons) button.SetPressedNoSignal(id == pageId);

        _pageTitle.Text = page.Title;
        _pageSubtitle.Text = page.Subtitle;
        _pageIcon.Texture = _ui.Icon(page.IconName);
        _scroll.ScrollVertical = 0;

        page.OnShown();
    }

    internal T GetPage<T>() where T : SettingsPage => _pages.OfType<T>().FirstOrDefault();

    // ---------------------------------------------------------------------
    // Refresh
    // ---------------------------------------------------------------------

    /// <summary>
    /// Coalesces bursts of change notifications (a single edit raises one immediately and one
    /// deferred from ProjectSettings) into one refresh at the end of the frame.
    /// </summary>
    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Callable.From(RefreshNow).CallDeferred();
    }

    private void RefreshNow()
    {
        _refreshQueued = false;
        if (!IsBuilt) return;

        Issues = ConfigurationAnalyzer.Analyze(_store);

        foreach (var page in _pages) page.Refresh();

        foreach (var (id, badge) in _navBadges)
        {
            var pageIssues = Issues.Where(i => i.PageId == id && i.Severity >= IssueSeverity.Warning).ToList();
            badge.Visible = pageIssues.Count > 0;
            if (pageIssues.Count == 0) continue;

            var worst = pageIssues.Max(i => i.Severity);
            badge.Text = pageIssues.Count.ToString();
            badge.AddThemeColorOverride("font_color", _ui.SeverityColor(worst));
            badge.TooltipText = string.Join("\n", pageIssues.Select(i => "• " + i.Title));
        }
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    private void BuildLayout()
    {
        var background = new Panel();
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        background.AddThemeStyleboxOverride("panel", _ui.Box(_ui.Base, null, 0, 0));
        AddChild(background);

        var outer = new VBoxContainer();
        outer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("separation", 0);
        AddChild(outer);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        outer.AddChild(body);

        body.AddChild(BuildSidebar());
        body.AddChild(BuildContent());
        outer.AddChild(BuildFooter());
    }

    private Control BuildSidebar()
    {
        var sidebar = new PanelContainer { CustomMinimumSize = new Vector2(_ui.S(230), 0) };
        var style = _ui.Box(_ui.SidebarBackground, null, 0, 0);
        style.ContentMarginTop = _ui.S(18);
        style.ContentMarginBottom = _ui.S(14);
        style.ContentMarginLeft = _ui.S(10);
        style.ContentMarginRight = _ui.S(10);
        sidebar.AddThemeStyleboxOverride("panel", style);

        var column = _ui.VBox(4);
        sidebar.AddChild(column);

        // Brand block
        var brand = _ui.VBox(0);
        var brandMargin = new MarginContainer();
        brandMargin.AddThemeConstantOverride("margin_left", _ui.S(8));
        brandMargin.AddThemeConstantOverride("margin_bottom", _ui.S(14));
        brandMargin.AddChild(brand);

        var name = _ui.Heading("Advanced Logger", 1.2f);
        brand.AddChild(name);
        brand.AddChild(_ui.Text("Version " + ReadPluginVersion() + " · Settings", subtle: true));
        column.AddChild(brandMargin);

        var group = new ButtonGroup();

        foreach (var page in _pages)
        {
            var button = new Button
            {
                Text = page.Title,
                Icon = _ui.Icon(page.IconName),
                ToggleMode = true,
                ButtonGroup = group,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, _ui.S(34)),
                FocusMode = Control.FocusModeEnum.All,
                ExpandIcon = false,
            };
            button.AddThemeConstantOverride("h_separation", _ui.S(10));
            button.AddThemeStyleboxOverride("normal", NavBox(Colors.Transparent));
            button.AddThemeStyleboxOverride("hover", NavBox(new Color(_ui.Font, 0.06f)));
            button.AddThemeStyleboxOverride("pressed", NavBox(new Color(_ui.Accent, 0.2f), _ui.Accent));
            button.AddThemeStyleboxOverride("hover_pressed", NavBox(new Color(_ui.Accent, 0.26f), _ui.Accent));
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            string id = page.Id;
            button.Pressed += () => Navigate(id);

            var badge = new Label
            {
                Visible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            if (_ui.BoldFont != null) badge.AddThemeFontOverride("font", _ui.BoldFont);
            badge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
            badge.OffsetLeft = -_ui.S(36);
            badge.OffsetRight = -_ui.S(10);
            button.AddChild(badge);

            _navButtons[id] = button;
            _navBadges[id] = badge;
            column.AddChild(button);
        }

        column.AddChild(EditorUi.Spacer(horizontal: false));

        var docs = _ui.IconButton("ExternalLink", EditorUi.DocsUrl, "Documentation");
        docs.Flat = true;
        docs.Alignment = HorizontalAlignment.Left;
        docs.Pressed += () => OS.ShellOpen(EditorUi.DocsUrl);
        column.AddChild(docs);

        return sidebar;
    }

    private StyleBoxFlat NavBox(Color background, Color? stripe = null)
    {
        var box = _ui.Box(background, null, 6, 0);
        box.ContentMarginLeft = _ui.S(10);
        box.ContentMarginRight = _ui.S(10);
        box.ContentMarginTop = _ui.S(6);
        box.ContentMarginBottom = _ui.S(6);

        if (stripe.HasValue)
        {
            box.BorderColor = stripe.Value;
            box.BorderWidthLeft = _ui.S(3);
        }

        return box;
    }

    private Control BuildContent()
    {
        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 0);

        // Page header
        var headerMargin = new MarginContainer();
        headerMargin.AddThemeConstantOverride("margin_left", _ui.S(28));
        headerMargin.AddThemeConstantOverride("margin_right", _ui.S(28));
        headerMargin.AddThemeConstantOverride("margin_top", _ui.S(22));
        headerMargin.AddThemeConstantOverride("margin_bottom", _ui.S(14));

        var header = _ui.HBox(14);
        _pageIcon = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            CustomMinimumSize = new Vector2(_ui.S(28), _ui.S(28)),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        header.AddChild(_pageIcon);

        var titles = _ui.VBox(0);
        _pageTitle = _ui.Heading("", 1.55f);
        _pageSubtitle = _ui.Text("", subtle: true, wrap: true);
        titles.AddChild(_pageTitle);
        titles.AddChild(_pageSubtitle);
        titles.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(titles);

        headerMargin.AddChild(header);
        content.AddChild(headerMargin);
        content.AddChild(new HSeparator());

        // Scrollable page body
        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };

        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", _ui.S(28));
        margin.AddThemeConstantOverride("margin_right", _ui.S(28));
        margin.AddThemeConstantOverride("margin_top", _ui.S(20));
        margin.AddThemeConstantOverride("margin_bottom", _ui.S(28));
        _scroll.AddChild(margin);

        var stack = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        margin.AddChild(stack);

        foreach (var page in _pages)
        {
            var root = page.Build(_ui, _store, this);
            root.Visible = false;
            _pageRoots[page.Id] = root;
            stack.AddChild(root);
        }

        content.AddChild(_scroll);
        return content;
    }

    private Control BuildFooter()
    {
        var footer = new PanelContainer();
        var style = _ui.Box(_ui.SidebarBackground, null, 0, 0);
        style.ContentMarginLeft = _ui.S(16);
        style.ContentMarginRight = _ui.S(12);
        style.ContentMarginTop = _ui.S(8);
        style.ContentMarginBottom = _ui.S(8);
        footer.AddThemeStyleboxOverride("panel", style);

        var row = _ui.HBox(10);
        footer.AddChild(row);

        row.AddChild(_ui.IconRect(_ui.Icon("Save") ?? _ui.Icon("Load"), _ui.Subtle));
        row.AddChild(_ui.Text("Changes are saved to project.godot automatically and apply the next time the game starts.", subtle: true));
        row.AddChild(EditorUi.Spacer());

        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(_ui.S(90), 0) };
        close.Pressed += Hide;
        row.AddChild(close);

        return footer;
    }

    private static string ReadPluginVersion()
    {
        var config = new ConfigFile();
        return config.Load(PluginConfigPath) == Error.Ok
            ? config.GetValue("plugin", "version", "?").AsString()
            : "?";
    }
}
#endif
