using System;
using System.Collections.Generic;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Built-in "Watch" panel: a live table of values registered by gameplay code through
/// <see cref="DebugScreen.Watch"/>.
/// </summary>
/// <example>
/// <code>
/// DebugScreen.Instance?.Watch("Player HP", () => _player.Health);
/// DebugScreen.Instance?.Watch("Enemies",   () => _enemies.Count);
/// DebugScreen.Instance?.Watch("State",     () => _stateMachine.CurrentState);
/// </code>
/// </example>
public class WatchDebugPanel : IDebugPanel
{
    /// <summary>Providers are polled at this interval, not every frame, to stay readable and cheap.</summary>
    private const float RefreshIntervalSeconds = 0.1f;

    private sealed class WatchEntry
    {
        public string Label { get; init; }
        public Func<object> Provider { get; set; }
        public Label ValueLabel { get; set; }
    }

    private readonly List<WatchEntry> _watches = new();
    private readonly Dictionary<string, WatchEntry> _byLabel = new(StringComparer.Ordinal);

    private GridContainer _grid;
    private Label _emptyLabel;
    private bool _rowsDirty;
    private float _timeSinceRefresh;

    public string PanelName => "Watch";

    public void OnPanelShow() => Evaluate();
    public void OnPanelHide() { }

    public Control GetPanelUi()
    {
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        var toolbar = new HBoxContainer();

        var clearButton = new Button { Text = "Clear", TooltipText = "Remove every registered watch" };
        clearButton.Pressed += Clear;
        toolbar.AddChild(clearButton);

        toolbar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        root.AddChild(toolbar);
        root.AddChild(new HSeparator());

        _emptyLabel = new Label
        {
            Text = "No watches registered.\n\nRegister one from your game code:\n" +
                   "    DebugScreen.Instance?.Watch(\"Player HP\", () => _player.Health);",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(_emptyLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,

            // Watched values change length constantly; they wrap rather than widening
            // the panel, so a horizontal scrollbar is never wanted.
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };

        _grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        scroll.AddChild(_grid);
        root.AddChild(scroll);

        _rowsDirty = true;

        return root;
    }

    /// <summary>
    /// Registers or replaces a watched value. Safe to call before the UI exists - rows are
    /// built lazily the next time the panel updates.
    /// </summary>
    public void Add(string label, Func<object> provider)
    {
        if (string.IsNullOrEmpty(label) || provider == null) return;

        if (_byLabel.TryGetValue(label, out var existing))
        {
            existing.Provider = provider;
            return;
        }

        var entry = new WatchEntry { Label = label, Provider = provider };
        _watches.Add(entry);
        _byLabel[label] = entry;
        _rowsDirty = true;
    }

    public bool Remove(string label)
    {
        if (string.IsNullOrEmpty(label) || !_byLabel.Remove(label, out var entry)) return false;

        _watches.Remove(entry);
        _rowsDirty = true;
        return true;
    }

    public void Clear()
    {
        _watches.Clear();
        _byLabel.Clear();
        _rowsDirty = true;
    }

    public void OnPanelUpdate(float delta)
    {
        if (_rowsDirty) RebuildRows();

        _timeSinceRefresh += delta;
        if (_timeSinceRefresh < RefreshIntervalSeconds) return;

        _timeSinceRefresh = 0f;
        Evaluate();
    }

    private void RebuildRows()
    {
        _rowsDirty = false;

        if (_grid == null) return;

        foreach (var child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }

        var monoFont = new SystemFont { FontNames = ["Consolas", "Courier New", "Monospace"] };

        foreach (var entry in _watches)
        {
            _grid.AddChild(new Label { Text = "  " + entry.Label });

            var value = new Label
            {
                Text = "-",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            value.AddThemeFontOverride("font", monoFont);

            _grid.AddChild(value);
            entry.ValueLabel = value;
        }

        if (_emptyLabel != null) _emptyLabel.Visible = _watches.Count == 0;

        Evaluate();
    }

    /// <remarks>
    /// Each provider is invoked inside its own try/catch: a watch on a freed node or a
    /// null field is a normal thing to hit while debugging, and it must show as an inline
    /// error rather than take down the overlay that is meant to help diagnose it.
    /// </remarks>
    private void Evaluate()
    {
        foreach (var entry in _watches)
        {
            if (entry.ValueLabel == null) continue;

            try
            {
                object value = entry.Provider();
                entry.ValueLabel.Text = value?.ToString() ?? "null";
            }
            catch (Exception e)
            {
                entry.ValueLabel.Text = $"<{e.GetType().Name}: {e.Message}>";
            }
        }
    }
}
