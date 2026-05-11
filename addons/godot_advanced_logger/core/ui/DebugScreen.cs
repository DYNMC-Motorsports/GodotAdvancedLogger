using System;
using System.Collections.Concurrent;
using Godot;
using System.Collections.Generic;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Autoload node, displaying registered debug panels and logs. Toggle visibility with F12.
/// </summary>
public partial class DebugScreen : CanvasLayer
{
    public static DebugScreen Instance { get; private set; }

    private const string SettingHotkeyPath = "addons/godot_advanced_logger/settings/ingame_ui_hotkey";

    private Control _mainContainer;
    private HBoxContainer _tabButtonContainer;
    private MarginContainer _panelContentContainer;
    
    private Dictionary<string, IDebugPanel> _registeredPanels = new();
    private Dictionary<string, Control> _panelUIs = new();
    
    private IDebugPanel _currentPanel;
    private LogsDebugPanel _logsPanel;

    private ConcurrentQueue<LogEntry> _pendingLogs = new();

    private ContextLogger _logger = new ContextLogger("DebugScreen");

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

    private void LoadHotkeyFromSettings()
    {
        if (ProjectSettings.HasSetting(SettingHotkeyPath))
        {
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
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } keyEvent || keyEvent.Keycode != _toggleHotkey) return;
        _mainContainer.Visible = !_mainContainer.Visible;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        while (_pendingLogs.TryDequeue(out var entry))
        {
            _logsPanel?.ProcessLog(entry);
        }
        
        if (!_mainContainer.Visible) return;
        
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

        _logger.Info($"Registered debug pane", new Dictionary<string, object>
        {
            { "panelName", panel.PanelName },
        });
    }

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
    
    public void EnqueueLog(LogEntry entry)
    {
        _pendingLogs.Enqueue(entry);
    }
    
    public IDebugPanel GetPanel(string panelName)
    {
        _registeredPanels.TryGetValue(panelName, out var panel);
        return panel;
    }
    
    public LogsDebugPanel GetLogsPanel() => _logsPanel;
}