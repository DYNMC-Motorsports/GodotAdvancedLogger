using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Autoload node hosting the in-game debug overlay. Toggle visibility with F12
/// (or the key configured in Project Settings).
/// </summary>
/// <remarks>
/// The overlay is a tabbed shell; every tab is an <see cref="IDebugPanel"/>. Five panels
/// ship with the plugin (Logs, Console, Watch, Performance, System) and you can add your
/// own with <see cref="RegisterDebugPanel"/>.
/// </remarks>
public partial class DebugScreen : CanvasLayer
{
    public static DebugScreen Instance { get; private set; }

    /// <summary>
    /// Upper bound on entries moved into the UI per frame. A burst of logs (a loop that
    /// logs every iteration, an exception storm) would otherwise be drained in a single
    /// frame and visibly stall the game.
    /// </summary>
    private const int MaxLogsPerFrame = 200;

    /// <summary>
    /// Upper bound on entries buffered between frames, so a flood cannot grow the queue
    /// without limit while the drain is rate-limited.
    /// </summary>
    private const int MaxPendingLogs = 5_000;

    /// <summary>Left anchor of the overlay in its narrow state (covers the right 40%).</summary>
    private const float NarrowAnchorLeft = 0.6f;

    private Control _mainContainer;
    private TabContainer _tabContainer;
    private Button _widthButton;

    private readonly Dictionary<string, IDebugPanel> _registeredPanels = new();
    private readonly Dictionary<string, Control> _panelUIs = new();

    /// <summary>Maps a panel UI's Godot instance id back to its panel name, for tab-change lookups.</summary>
    private readonly Dictionary<ulong, string> _uiToPanelName = new();

    private IDebugPanel _currentPanel;

    /// <summary>Whether <see cref="IDebugPanel.OnPanelShow"/> has been delivered for <see cref="_currentPanel"/>.</summary>
    private bool _currentPanelShown;

    /// <summary>Panel callbacks already reported as faulting, so one broken panel logs once, not once per frame.</summary>
    private readonly HashSet<string> _reportedPanelFaults = new();

    private LogsDebugPanel _logsPanel;
    private WatchDebugPanel _watchPanel;
    private ConsoleDebugPanel _consolePanel;
    private PerformanceDebugPanel _performancePanel;
    private SystemInfoDebugPanel _systemPanel;

    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private int _pendingCount;

    private readonly ContextLogger _logger = new("DebugScreen");

    private Key _toggleHotkey = Key.F12;
    private bool _pauseWhileOpen;
    private bool _uiEnabled;
    private bool _isWide;
    private bool _treeWasPausedBeforeOpen;

    /// <summary>Whether the overlay is currently visible.</summary>
    public bool IsOpen => _mainContainer != null && _mainContainer.Visible;

    public override void _Ready()
    {
        if (Instance != null)
        {
            GD.PushWarning("DebugScreen: Instance already exists!");
            QueueFree();
            return;
        }

        Instance = this;
        Layer = 100;

        // Keep processing while the tree is paused, otherwise "pause while open" would
        // freeze the overlay along with the game.
        ProcessMode = ProcessModeEnum.Always;

        LoadSettings();

        if (!_uiEnabled)
        {
            GD.Print("[DebugScreen] Release build: overlay disabled. Enable it with " +
                     "'Project -> Tools -> Advanced Logger Settings... -> In-Game Overlay -> Allow in release exports'.");
            return;
        }

        // BuildUi creates the overlay already hidden.
        BuildUi();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void LoadSettings()
    {
        // The overlay is a developer tool. It stays out of release exports unless the
        // project explicitly opts in - matching how LogManager gates its dev-only writers.
        _uiEnabled = OS.IsDebugBuild() || LoggerSettings.GetBool(LoggerSettings.OverlayAllowInRelease);
        _pauseWhileOpen = LoggerSettings.GetBool(LoggerSettings.OverlayPauseGame);

        _toggleHotkey = LoggerSettings.GetOverlayHotkey(out bool parsed);

        if (parsed)
        {
            _logger.Debug($"Using hotkey: {_toggleHotkey}");
        }
        else
        {
            _logger.Warning($"Could not parse hotkey '{LoggerSettings.GetString(LoggerSettings.OverlayHotkey)}'. Falling back to F12.");
        }
    }

    /// <remarks>
    /// The hotkey is handled in <c>_ShortcutInput</c> rather than <c>_Input</c> so it still
    /// fires while a text field inside the overlay has focus - the console input and the
    /// log search box would otherwise swallow it.
    /// </remarks>
    public override void _ShortcutInput(InputEvent @event)
    {
        if (_mainContainer == null) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } keyEvent) return;
        if (keyEvent.Keycode != _toggleHotkey) return;
        if (ShouldIgnoreHotkey(keyEvent)) return;

        Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Prevents a single-character hotkey from toggling the overlay while the user is
    /// typing into it. Function keys and modified combinations always toggle.
    /// </summary>
    private bool ShouldIgnoreHotkey(InputEventKey keyEvent)
    {
        if (keyEvent.CtrlPressed || keyEvent.AltPressed || keyEvent.MetaPressed) return false;
        if (keyEvent.Keycode >= Key.F1 && keyEvent.Keycode <= Key.F16) return false;

        var focus = GetViewport()?.GuiGetFocusOwner();
        return focus is LineEdit or TextEdit;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (_mainContainer == null || _mainContainer.Visible) return;

        _mainContainer.Show();

        if (_pauseWhileOpen)
        {
            var tree = GetTree();
            if (tree != null)
            {
                _treeWasPausedBeforeOpen = tree.Paused;
                tree.Paused = true;
            }
        }

        // Resolves the active tab and delivers OnPanelShow, including for a tab the user
        // switched to while the overlay was hidden.
        SyncCurrentPanel();
    }

    public void Close()
    {
        if (_mainContainer == null || !_mainContainer.Visible) return;

        if (_currentPanelShown)
        {
            _currentPanelShown = false;
            SafeInvoke(_currentPanel, nameof(IDebugPanel.OnPanelHide), p => p.OnPanelHide());
        }

        _mainContainer.Hide();

        if (_pauseWhileOpen)
        {
            var tree = GetTree();
            // Restore rather than force-unpause: the game may have been paused for its
            // own reasons (pause menu) before the overlay was opened.
            if (tree != null) tree.Paused = _treeWasPausedBeforeOpen;
        }
    }

    public override void _Process(double delta)
    {
        int drained = 0;
        while (drained < MaxLogsPerFrame && _pendingLogs.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _pendingCount);
            _logsPanel?.ProcessLog(entry);
            drained++;
        }

        if (!IsOpen) return;

        // Re-resolve every frame rather than relying solely on the TabContainer's
        // tab_changed signal. If that signal is ever missed, a cached _currentPanel would
        // leave the tab the user is actually looking at frozen; polling self-heals.
        SyncCurrentPanel();

        if (_currentPanel == null) return;

        float frameDelta = (float)delta;
        SafeInvoke(_currentPanel, nameof(IDebugPanel.OnPanelUpdate), p => p.OnPanelUpdate(frameDelta));
    }

    /// <summary>
    /// Points <see cref="_currentPanel"/> at whatever tab is active, firing the
    /// show/hide hooks around the transition.
    /// </summary>
    private void SyncCurrentPanel()
    {
        if (_tabContainer == null || _tabContainer.GetTabCount() == 0) return;

        var control = _tabContainer.GetTabControl(_tabContainer.CurrentTab);
        if (control == null) return;

        if (!_uiToPanelName.TryGetValue(control.GetInstanceId(), out var panelName)) return;
        if (!_registeredPanels.TryGetValue(panelName, out var panel)) return;

        if (ReferenceEquals(panel, _currentPanel))
        {
            // Same panel, but the overlay may have just been opened.
            if (!IsOpen || _currentPanelShown) return;

            _currentPanelShown = true;
            SafeInvoke(_currentPanel, nameof(IDebugPanel.OnPanelShow), p => p.OnPanelShow());
            return;
        }

        if (_currentPanelShown)
        {
            _currentPanelShown = false;
            SafeInvoke(_currentPanel, nameof(IDebugPanel.OnPanelHide), p => p.OnPanelHide());
        }

        _currentPanel = panel;

        if (!IsOpen) return;

        _currentPanelShown = true;
        SafeInvoke(_currentPanel, nameof(IDebugPanel.OnPanelShow), p => p.OnPanelShow());
    }

    /// <summary>
    /// Runs a panel callback, containing any exception it throws.
    /// </summary>
    /// <remarks>
    /// Without this, a custom panel throwing from <c>OnPanelUpdate</c> would propagate out
    /// of <c>_Process</c> every frame - taking the log drain down with it and burying the
    /// output in duplicate stack traces. Each faulting callback is reported once.
    /// </remarks>
    private void SafeInvoke(IDebugPanel panel, string callbackName, Action<IDebugPanel> callback)
    {
        if (panel == null) return;

        try
        {
            callback(panel);
        }
        catch (Exception e)
        {
            string key = $"{panel.PanelName}.{callbackName}";
            if (!_reportedPanelFaults.Add(key)) return;

            GD.PushError($"[DebugScreen] Panel '{panel.PanelName}' threw from {callbackName}: {e}");
        }
    }

    private void BuildUi()
    {
        _mainContainer = new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.1f, 0.94f),

            // Stop: swallow clicks so they do not fall through to the game behind the overlay.
            MouseFilter = Control.MouseFilterEnum.Stop,

            // Built hidden. If the overlay counted as open while BuildUi ran, the initial
            // ShowPanel would deliver OnPanelShow to the Logs panel and mark it shown -
            // so the first real Open() would then skip the hook for every panel.
            Visible = false
        };

        // FullRect gives predictable zero offsets; the left anchor alone then controls width.
        _mainContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _mainContainer.AnchorLeft = NarrowAnchorLeft;
        _mainContainer.GrowHorizontal = Control.GrowDirection.Begin;
        _mainContainer.CustomMinimumSize = new Vector2(500, 0);

        AddChild(_mainContainer);

        var vSplit = new VBoxContainer();
        vSplit.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _mainContainer.AddChild(vSplit);

        vSplit.AddChild(BuildHeader());

        _tabContainer = new TabContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipTabs = false
        };

        // No tab_changed subscription: _Process resolves the active tab every frame, which
        // is one code path instead of two and cannot drift out of sync.
        vSplit.AddChild(_tabContainer);

        _logsPanel = new LogsDebugPanel();
        _consolePanel = new ConsoleDebugPanel();
        _watchPanel = new WatchDebugPanel();
        _performancePanel = new PerformanceDebugPanel();
        _systemPanel = new SystemInfoDebugPanel();

        RegisterDebugPanel(_logsPanel);
        RegisterDebugPanel(_consolePanel);
        RegisterDebugPanel(_watchPanel);
        RegisterDebugPanel(_performancePanel);
        RegisterDebugPanel(_systemPanel);

        DebugConsole.RegisterBuiltIns();

        ShowPanel(_logsPanel.PanelName);
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 32)
        };

        header.AddChild(new Label
        {
            Text = "  Godot Advanced Logger",
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        });

        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _widthButton = new Button { Text = "Wide", TooltipText = "Expand the overlay to full width" };
        _widthButton.Pressed += () => SetWide(!_isWide);
        header.AddChild(_widthButton);

        var closeButton = new Button { Text = "Close", TooltipText = $"Hide the overlay ({_toggleHotkey})" };
        closeButton.Pressed += Close;
        header.AddChild(closeButton);

        return header;
    }

    /// <summary>Switches the overlay between covering the right 40% and the full viewport.</summary>
    public void SetWide(bool wide)
    {
        if (_mainContainer == null) return;

        _isWide = wide;
        _mainContainer.AnchorLeft = wide ? 0.0f : NarrowAnchorLeft;

        if (_widthButton == null) return;
        _widthButton.Text = wide ? "Narrow" : "Wide";
        _widthButton.TooltipText = wide ? "Shrink the overlay to the right side" : "Expand the overlay to full width";
    }

    /// <summary>
    /// Adds a panel as a new tab. Panel names are unique; registering a name that already
    /// exists is a no-op.
    /// </summary>
    public void RegisterDebugPanel(IDebugPanel panel)
    {
        if (panel == null || _tabContainer == null) return;
        if (!_registeredPanels.TryAdd(panel.PanelName, panel)) return;

        // TabContainer requires a Control child per tab, so a panel that supplies no UI
        // still gets an empty placeholder rather than silently vanishing from the tab bar.
        var panelUi = panel.GetPanelUi() ?? new Control();

        panelUi.Name = SanitizeNodeName(panel.PanelName);
        panelUi.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panelUi.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        _tabContainer.AddChild(panelUi);
        _tabContainer.SetTabTitle(_tabContainer.GetTabCount() - 1, panel.PanelName);

        _panelUIs[panel.PanelName] = panelUi;
        _uiToPanelName[panelUi.GetInstanceId()] = panel.PanelName;

        _logger.Info("Registered debug panel", new Dictionary<string, object>
        {
            { "panelName", panel.PanelName },
        });
    }

    /// <summary>
    /// Removes a previously registered panel and frees its tab.
    /// </summary>
    /// <returns><c>true</c> if a panel with that name was registered and has been removed.</returns>
    public bool UnregisterDebugPanel(string panelName)
    {
        if (string.IsNullOrEmpty(panelName) || !_registeredPanels.TryGetValue(panelName, out var panel))
            return false;

        if (ReferenceEquals(_currentPanel, panel))
        {
            if (_currentPanelShown)
            {
                _currentPanelShown = false;
                SafeInvoke(_currentPanel, nameof(IDebugPanel.OnPanelHide), p => p.OnPanelHide());
            }

            _currentPanel = null;
        }

        _registeredPanels.Remove(panelName);

        if (_panelUIs.Remove(panelName, out var ui))
        {
            _uiToPanelName.Remove(ui.GetInstanceId());

            // Detach before freeing so TabContainer drops the tab in the same frame.
            _tabContainer?.RemoveChild(ui);
            ui.QueueFree();
        }

        // Never leave the overlay with no active panel.
        SyncCurrentPanel();

        return true;
    }

    /// <inheritdoc cref="UnregisterDebugPanel(string)"/>
    public bool UnregisterDebugPanel(IDebugPanel panel)
        => panel != null && UnregisterDebugPanel(panel.PanelName);

    /// <summary>Brings the named panel's tab to the front.</summary>
    public void ShowPanel(string panelName)
    {
        if (_tabContainer == null || string.IsNullOrEmpty(panelName)) return;
        if (!_panelUIs.TryGetValue(panelName, out var ui)) return;

        int index = FindTabIndex(ui);
        if (index < 0) return;

        _tabContainer.CurrentTab = index;
        SyncCurrentPanel();
    }

    /// <summary>
    /// Locates a panel UI's tab index by scanning the tab controls.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Control.GetIndex()</c>: that is the child index, and
    /// TabContainer keeps an internal TabBar child, so the two are only guaranteed to
    /// agree as long as internal nodes are excluded. Scanning the tab controls asks the
    /// question directly.
    /// </remarks>
    private int FindTabIndex(Control ui)
    {
        if (ui == null) return -1;

        ulong id = ui.GetInstanceId();

        for (int i = 0; i < _tabContainer.GetTabCount(); i++)
        {
            var control = _tabContainer.GetTabControl(i);
            if (control != null && control.GetInstanceId() == id) return i;
        }

        return -1;
    }

    /// <summary>
    /// Godot node names cannot contain <c>. : @ / " %</c>; panel names are free-form, so
    /// the node name is sanitized while the tab title keeps the original text.
    /// </summary>
    private static string SanitizeNodeName(string panelName)
    {
        if (string.IsNullOrEmpty(panelName)) return "Panel";

        var chars = panelName.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            switch (chars[i])
            {
                case '.':
                case ':':
                case '@':
                case '/':
                case '"':
                case '%':
                    chars[i] = '_';
                    break;
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Queues an entry for display. Safe to call from any thread; the entry is handed to
    /// the UI on the next frame.
    /// </summary>
    public void EnqueueLog(LogEntry entry)
    {
        if (!_uiEnabled) return;

        if (Interlocked.Increment(ref _pendingCount) > MaxPendingLogs)
        {
            // Backlog is already beyond what the UI can show; drop the newest rather
            // than let a runaway logger grow this queue unbounded.
            Interlocked.Decrement(ref _pendingCount);
            return;
        }

        _pendingLogs.Enqueue(entry);
    }

    // ---------------------------------------------------------------------
    // Watch API
    // ---------------------------------------------------------------------

    /// <summary>
    /// Registers a value to display live in the Watch panel. The provider is invoked once
    /// per frame while the panel is visible, so keep it cheap.
    /// </summary>
    /// <param name="label">Row label. Registering the same label twice replaces the provider.</param>
    /// <param name="provider">Returns the current value; exceptions are caught and shown inline.</param>
    /// <example>
    /// <code>DebugScreen.Instance?.Watch("Player HP", () => _player.Health);</code>
    /// </example>
    public void Watch(string label, Func<object> provider) => _watchPanel?.Add(label, provider);

    /// <summary>Removes a watch previously added with <see cref="Watch"/>.</summary>
    public bool Unwatch(string label) => _watchPanel?.Remove(label) ?? false;

    /// <summary>Removes every registered watch.</summary>
    public void ClearWatches() => _watchPanel?.Clear();

    // ---------------------------------------------------------------------
    // GDScript bridge
    // ---------------------------------------------------------------------
    //
    // GDScript cannot call static methods or pass a Func<object>, but it can call public
    // instance methods on an autoload node. Unwatch, ClearWatches, ShowPanel, Open, Close
    // and Toggle already take Variant-marshallable parameters and are callable as they are;

    /// <summary>
    /// GDScript entry point for registering a console command.
    /// </summary>
    /// <param name="handler">
    /// A plain object+method callable on the GDScript relay, invoked as
    /// <c>handler(name, args)</c>. It is deliberately not the user's own callable: a
    /// GDScript lambda is a custom callable that cannot survive marshalling into
    /// <see cref="Callable"/> and would arrive here as an empty <c>null::null</c>.
    /// See <c>GodotLoggerBridge.gd</c>.
    /// </param>
    public void GdRegisterCommand(string name, Callable handler, string description, string usage)
    {
        DebugConsole.Register(name, args =>
        {
            var gdArgs = new Godot.Collections.Array();
            foreach (string arg in args) gdArgs.Add(arg);

            // The name goes back across so the relay knows which handler to dispatch to.
            Variant result = handler.Call(name, gdArgs);
            return result.VariantType == Variant.Type.Nil ? null : result.ToString();
        }, description, usage);
    }

    /// <summary>
    /// GDScript entry point for <see cref="Watch"/>.
    /// </summary>
    /// <param name="label">Row label, also passed to <paramref name="provider"/> on each poll.</param>
    /// <param name="provider">
    /// A plain object+method callable on the GDScript relay, invoked as
    /// <c>provider(label)</c>. As above, the user's own lambda cannot cross the boundary.
    /// </param>
    public void GdWatch(string label, Callable provider)
        => _watchPanel?.Add(label, () => VariantConverter.ToObject(provider.Call(label)));

    // ---------------------------------------------------------------------
    // Accessors
    // ---------------------------------------------------------------------

    public IDebugPanel GetPanel(string panelName)
    {
        if (string.IsNullOrEmpty(panelName)) return null;

        _registeredPanels.TryGetValue(panelName, out var panel);
        return panel;
    }

    /// <summary>Names of every registered panel, in tab order.</summary>
    public List<string> GetPanelNames()
    {
        var names = new List<string>(_registeredPanels.Count);
        if (_tabContainer == null) return names;

        for (int i = 0; i < _tabContainer.GetTabCount(); i++)
        {
            var control = _tabContainer.GetTabControl(i);
            if (control != null && _uiToPanelName.TryGetValue(control.GetInstanceId(), out var name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public LogsDebugPanel GetLogsPanel() => _logsPanel;
    public ConsoleDebugPanel GetConsolePanel() => _consolePanel;
    public WatchDebugPanel GetWatchPanel() => _watchPanel;
    public PerformanceDebugPanel GetPerformancePanel() => _performancePanel;
    public SystemInfoDebugPanel GetSystemInfoPanel() => _systemPanel;
}
