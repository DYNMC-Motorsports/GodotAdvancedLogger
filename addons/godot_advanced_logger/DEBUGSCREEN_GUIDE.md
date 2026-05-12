# DebugScreen - Additional Guide

## Overview

The `DebugScreen` is the in-game debug overlay for this logging plugin. It is designed as a panel-based UI that lets you inspect logs and build custom debugging tools while the game is running.

The overlay is implemented as a `CanvasLayer` autoload and can be toggled with **F12**. By default, it starts hidden and shows a tabbed interface on the right side of the screen.

## Key Features

- **Tabbed panel system**: Multiple debug panels can live inside one shared overlay.
- **Extensible by design**: Add your own panels by implementing `IDebugPanel`.
- **Log filtering**: The built-in Logs panel can filter by log level and channel.
- **Color-coded output**: Log entries are colorized by severity.
- **Main-thread UI updates**: Log entries are queued and processed safely on the main thread.
- **Automatic log routing**: The logging pipeline includes a `DebugScreenWriter` so logs can appear in the overlay automatically when the screen exists.

## Default Panel: Logs

The built-in `Logs` panel shows log entries produced by the logging system.

### What it provides

- Auto-scrolling log output
- Toggle filters for individual log levels
- Toggle filters for individual channels
- A capped history of **1000 lines** to prevent unbounded memory growth
- Rich text formatting with timestamps, levels, channels, and messages

### Filtering behavior

- When a new channel appears, it is added to the channel filter list automatically.
- By default, newly discovered channels and all log levels are enabled.
- Changing any filter rebuilds the visible log output from the stored history.

## How logging reaches the overlay

The `LogManager` registers the default writers during initialization, including the `DebugScreenWriter`. That writer forwards log entries to `DebugScreen.EnqueueLog(entry)` if the debug overlay is available.

In practice, this means you can continue using the plugin's normal `ContextLogger` or `LogManager` flow, and the overlay will display the same logs without additional integration work.

Example:

```csharp
private readonly ContextLogger _logger = new ContextLogger("Combat");

_logger.Info("Player entered combat", new Dictionary<string, object>
{
    { "EnemyCount", 3 },
    { "Area", "Arena" }
});
```

## Creating Custom Panels

Custom panels are created by implementing `IDebugPanel` and registering the panel instance with the `DebugScreen`.

### Step 1: Implement `IDebugPanel`

```csharp
using Godot;

public partial class MyCustomPanel : IDebugPanel
{
    private Label _statusLabel;

    public string PanelName => "My Panel";

    public void OnPanelShow()
    {
        // Called when this panel becomes visible.
    }

    public void OnPanelHide()
    {
        // Called when this panel is hidden.
    }

    public void OnPanelUpdate(float delta)
    {
        // Called every frame while the panel is visible.
        // Keep this lightweight.
    }

    public Control GetPanelUi()
    {
        var container = new VBoxContainer();
        _statusLabel = new Label { Text = "Waiting for data..." };
        container.AddChild(_statusLabel);
        return container;
    }
}
```

### Step 2: Register the panel

Register the panel from initialization code, for example in `_Ready()`:

```csharp
var myPanel = new MyCustomPanel();
DebugScreen.Instance.RegisterDebugPanel(myPanel);
```

### Panel registration rules

- `PanelName` must be unique.
- If a panel with the same name already exists, the registration attempt is ignored.
- `GetPanelUi()` is called when the panel is registered, not every frame.
- If `GetPanelUi()` returns `null`, the tab still exists, but no custom UI is shown.

## Example Panels

### Example 1: Hover unit information

This pattern is useful for strategy games or entity-heavy games where you want to inspect the object under the mouse.

```csharp
using Godot;

public partial class HoverUnitInfoPanel : IDebugPanel
{
    private Label _infoLabel;
    private Unit _hoveredUnit;

    public string PanelName => "Unit Info";

    public void OnPanelShow()
    {
        // Connect signals or begin tracking state here.
    }

    public void OnPanelHide()
    {
        // Disconnect signals or stop tracking state here.
    }

    public void OnPanelUpdate(float delta)
    {
        var mousePos = GetGlobalMousePosition();
        _hoveredUnit = FindUnitAtPosition(mousePos);

        if (_hoveredUnit != null)
        {
            _infoLabel.Text = $"Unit: {_hoveredUnit.Name}\n" +
                              $"HP: {_hoveredUnit.Health}/{_hoveredUnit.MaxHealth}\n" +
                              $"Morale: {_hoveredUnit.Morale}";
        }
        else
        {
            _infoLabel.Text = "Hover over a unit...";
        }
    }

    public Control GetPanelUi()
    {
        var container = new VBoxContainer();
        _infoLabel = new Label { Text = "Waiting for data..." };
        container.AddChild(_infoLabel);
        return container;
    }

    private Unit FindUnitAtPosition(Vector2 pos)
    {
        // Replace with your own selection / raycast logic.
        return null;
    }
}
```

### Example 2: Performance panel

```csharp
using Godot;

public partial class PerformanceDebugPanel : IDebugPanel
{
    private Label _statsLabel;

    public string PanelName => "Performance";

    public void OnPanelShow() { }
    public void OnPanelHide() { }

    public void OnPanelUpdate(float delta)
    {
        double fps = Engine.GetFramesPerSecond();
        ulong memory = GD.GetStaticMemoryUsage();

        _statsLabel.Text = $"FPS: {fps}\n" +
                           $"Memory: {memory / 1024 / 1024} MB\n" +
                           $"Nodes: {GetTree().NodeCount}";
    }

    public Control GetPanelUi()
    {
        var container = new VBoxContainer();
        _statsLabel = new Label { Text = "Loading..." };
        container.AddChild(_statsLabel);
        return container;
    }
}
```

### Example 3: Game state overview

```csharp
using Godot;

public partial class GameStatesPanel : IDebugPanel
{
    private ItemList _statesList;

    public string PanelName => "Game States";

    public Control GetPanelUi()
    {
        var container = new VBoxContainer();
        container.AddChild(new Label { Text = "Current Game States" });

        _statesList = new ItemList { CustomMinimumSize = new Vector2(0, 300) };
        container.AddChild(_statesList);

        return container;
    }

    public void OnPanelUpdate(float delta)
    {
        _statesList.Clear();

        // Replace these with real game state values.
        _statesList.AddItem("GameMode: Combat");
        _statesList.AddItem("Paused: false");
        _statesList.AddItem("Current Level: 5");
        _statesList.AddItem("Player Position: (100, 50)");
    }

    public void OnPanelShow() { }
    public void OnPanelHide() { }
}
```

## Keyboard Shortcut

- **F12**: Toggle the `DebugScreen` overlay on and off.

## API Reference

### `DebugScreen`

```csharp
// Register a custom debug panel.
DebugScreen.Instance.RegisterDebugPanel(panel);

// Show a panel by name. If it is already visible, switch to it.
DebugScreen.Instance.ShowPanel("PanelName");

// Get a registered panel by name.
IDebugPanel panel = DebugScreen.Instance.GetPanel("PanelName");

// Get the built-in Logs panel.
LogsDebugPanel logsPanel = DebugScreen.Instance.GetLogsPanel();

// Enqueue a log entry for the overlay.
DebugScreen.Instance.EnqueueLog(entry);
```

### `IDebugPanel`

```csharp
public interface IDebugPanel
{
    string PanelName { get; }
    void OnPanelShow();
    void OnPanelHide();
    void OnPanelUpdate(float delta);
    Control GetPanelUi();
}
```

## Best Practices

1. **Keep `OnPanelUpdate()` light**
   - It runs every frame while the panel is visible.
   - Avoid expensive scene queries or heavy UI rebuilding.

2. **Use `OnPanelShow()` and `OnPanelHide()` for setup and cleanup**
   - Connect and disconnect signals here.
   - Allocate and free temporary resources here if needed.

3. **Use unique panel names**
   - The tab label comes from `PanelName`.
   - Duplicate names will not register twice.

4. **Update UI on the main thread**
   - Godot controls should be created and modified from the main thread.
   - If you are collecting data from other threads, pass it to the panel safely first.

5. **Prefer the logging pipeline for log output**
   - Use `ContextLogger` / `LogManager` for normal logging.
   - Use `EnqueueLog()` only when you need to push an entry directly into the overlay.

6. **Keep the overlay focused**
   - Use the built-in Logs panel for log inspection.
   - Add custom panels only for information that is useful during development or debugging.

## Notes and Common Pitfalls

- `GetPanelUi()` is the correct method name in this codebase.
- `EnqueueLog()` is the correct method name on `DebugScreen`.
- The overlay is hidden by default and must be opened with **F12** or shown programmatically.
- The `Logs` panel is created automatically when `DebugScreen` initializes.

## Minimal Usage Example

```csharp
public override void _Ready()
{
    var panel = new MyCustomPanel();
    DebugScreen.Instance.RegisterDebugPanel(panel);
}
```

That is all you need for a basic custom panel. Once registered, it appears as a tab next to the built-in `Logs` panel.
