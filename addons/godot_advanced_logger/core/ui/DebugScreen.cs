using System;
using System.Collections.Concurrent;
using Godot;
using System.Collections.Generic;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Autoload node, displaying registered debug panels and logs. Toggle visibility with F12
/// (or the key configured in Project Settings).
/// </summary>
public partial class DebugScreen : CanvasLayer
{
    public static DebugScreen Instance { get; private set; }

    private const string SettingHotkeyPath = "addons/godot_advanced_logger/settings/ingame_ui_hotkey";
    
    private const int MaxLogsPerFrame = 200;
    private const int MaxPendingLogs = 5_000;

    private Control _mainContainer;
    private HBoxContainer _tabButtonContainer;
    private MarginContainer _panelContentContainer;

    private readonly Dictionary<string, IDebugPanel> _registeredPanels = new();
    private readonly Dictionary<string, Control> _panelUIs = new();
    private readonly Dictionary<string, Button> _panelButtons = new();

    private IDebugPanel _currentPanel;
    private LogsDebugPanel _logsPanel;

    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private int _pendingCount;

    private readonly ContextLogger _logger = new("DebugScreen");

    private Key _toggleHotkey = Key.F12;

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

        LoadHotkeyFromSettings();

        BuildUi();

        _mainContainer.Hide();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void LoadHotkeyFromSettings()
    {
        if (!ProjectSettings.HasSetting(SettingHotkeyPath)) return;

        string hotkeyStr = ProjectSettings.GetSetting(SettingHotkeyPath).AsString();

        if (Enum.TryParse(typeof(Key), hotkeyStr, true, out var result))
        {
            _toggleHotkey = (Key)result;
            _logger.Debug($"Using custom hotkey: {_toggleHotkey}");
        }
        else
        {
            _logger.Warning($"Could not parse hotkey '{hotkeyStr}'. Falling back to F12.");
        }
    }

    public override void _Input(InputEvent @event)
    {
        // A duplicate instance frees itself in _Ready before building any UI, but can
        // still receive input during the frame before it is actually freed.
        if (_mainContainer == null) return;

        if (@event is not InputEventKey { Pressed: true } keyEvent || keyEvent.Keycode != _toggleHotkey) return;

        _mainContainer.Visible = !_mainContainer.Visible;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        int drained = 0;
        while (drained < MaxLogsPerFrame && _pendingLogs.TryDequeue(out var entry))
        {
            System.Threading.Interlocked.Decrement(ref _pendingCount);
            _logsPanel?.ProcessLog(entry);
            drained++;
        }

        if (_mainContainer == null || !_mainContainer.Visible) return;

        _currentPanel?.OnPanelUpdate((float)delta);
    }

    private void BuildUi()
    {
        _mainContainer = new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.1f, 0.9f)
        };
        _mainContainer.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _mainContainer.AnchorLeft = 0.6f;
        _mainContainer.AnchorRight = 1.0f;
        _mainContainer.GrowHorizontal = Control.GrowDirection.Begin;
        _mainContainer.CustomMinimumSize = new Vector2(500, 0);

        AddChild(_mainContainer);

        var vSplit = new VBoxContainer();
        vSplit.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mainContainer.AddChild(vSplit);

        _tabButtonContainer = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 40),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        vSplit.AddChild(_tabButtonContainer);

        var separator = new HSeparator();
        vSplit.AddChild(separator);

        _panelContentContainer = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        vSplit.AddChild(_panelContentContainer);

        _logsPanel = new LogsDebugPanel();
        RegisterDebugPanel(_logsPanel);
        ShowPanel("Logs");
    }

    /// <summary>
    /// Adds a panel and its tab button. Panel names are unique; registering a name that
    /// already exists is a no-op.
    /// </summary>
    public void RegisterDebugPanel(IDebugPanel panel)
    {
        if (panel == null || !_registeredPanels.TryAdd(panel.PanelName, panel))
            return;

        var panelUi = panel.GetPanelUi();
        if (panelUi != null)
        {
            panelUi.Hide();
            _panelContentContainer.AddChild(panelUi);
            _panelUIs[panel.PanelName] = panelUi;
        }

        var tabButton = new Button
        {
            Text = panel.PanelName,
            CustomMinimumSize = new Vector2(100, 0)
        };

        tabButton.Pressed += () => ShowPanel(panel.PanelName);
        _tabButtonContainer.AddChild(tabButton);
        _panelButtons[panel.PanelName] = tabButton;

        _logger.Info("Registered debug panel", new Dictionary<string, object>
        {
            { "panelName", panel.PanelName },
        });
    }

    /// <summary>
    /// Removes a previously registered panel, freeing its UI and tab button.
    /// </summary>
    /// <returns><c>true</c> if a panel with that name was registered and has been removed.</returns>
    public bool UnregisterDebugPanel(string panelName)
    {
        if (string.IsNullOrEmpty(panelName) || !_registeredPanels.TryGetValue(panelName, out var panel))
            return false;

        if (_currentPanel == panel)
        {
            _currentPanel.OnPanelHide();
            _currentPanel = null;
        }

        _registeredPanels.Remove(panelName);

        if (_panelUIs.Remove(panelName, out var ui))
        {
            ui.QueueFree();
        }

        if (_panelButtons.Remove(panelName, out var button))
        {
            button.QueueFree();
        }

        // Never leave the overlay with no active panel.
        if (_currentPanel == null && _logsPanel != null && panelName != _logsPanel.PanelName)
        {
            ShowPanel(_logsPanel.PanelName);
        }

        return true;
    }

    /// <inheritdoc cref="UnregisterDebugPanel(string)"/>
    public bool UnregisterDebugPanel(IDebugPanel panel)
        => panel != null && UnregisterDebugPanel(panel.PanelName);

    public void ShowPanel(string panelName)
    {
        if (!_registeredPanels.TryGetValue(panelName, out var panel))
            return;

        if (_currentPanel != null)
        {
            _currentPanel.OnPanelHide();
            if (_panelUIs.TryGetValue(_currentPanel.PanelName, out var oldUi))
            {
                oldUi.Hide();
            }
        }

        _currentPanel = panel;
        if (_panelUIs.TryGetValue(_currentPanel.PanelName, out var newUi))
        {
            newUi.Show();
        }

        _currentPanel.OnPanelShow();
    }

    /// <summary>
    /// Queues an entry for display. Safe to call from any thread; the entry is handed to
    /// the UI on the next frame.
    /// </summary>
    public void EnqueueLog(LogEntry entry)
    {
        if (System.Threading.Interlocked.Increment(ref _pendingCount) > MaxPendingLogs)
        {
            // Backlog is already beyond what the UI can show; drop the newest rather
            // than let a runaway logger grow this queue unbounded.
            System.Threading.Interlocked.Decrement(ref _pendingCount);
            return;
        }

        _pendingLogs.Enqueue(entry);
    }

    public IDebugPanel GetPanel(string panelName)
    {
        _registeredPanels.TryGetValue(panelName, out var panel);
        return panel;
    }

    public LogsDebugPanel GetLogsPanel() => _logsPanel;
}
