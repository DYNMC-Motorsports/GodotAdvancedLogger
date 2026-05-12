# Godot Advanced Logger (C#)

Godot Advanced Logger is a robust, high-performance, and structured logging framework designed specifically for Godot 4.x C# projects. 

Moving beyond standard `GD.Print` console outputs, this plugin introduces a standard logging architecture. It is built from the ground up to prevent garbage collector spikes (Zero-Allocation), support structured data (JSON), stream logs in real-time to external dashboards like Seq, and provide a fully extensible **In-Game Debug UI**.

## Key Features

## Key Features
* **Zero-Cost Debugging:** `Debug()` calls utilize C#'s `[Conditional("DEBUG")]` attribute. In your final Release builds, the compiler completely strips these calls, ensuring **zero** CPU overhead and memory allocation.
* **In-Game Debug Screen:** A toggleable, right-aligned overlay to read logs directly inside your standalone game exports. Fully extensible via the `IDebugPanel` API to add your own custom developer tools.
* **Production Safe:** Automatically detects Release exports (`OS.IsDebugBuild()`) and hard-disables developer tools (like the UI and network writers) while intelligently throttling log verbosity based on separate Editor/Release settings.
* **Structured Logging:** Stop parsing messy text strings. Send exact data points via C# Dictionaries, making your logs fully searchable and filterable in external tools.
* **Multi-Target Output:** Write simultaneously to:
  * Godot Console (with Rich Text BBCode colors)
  * In-Game UI Overlay
  * Text Files (`.txt` for quick reading)
  * JSON Lines Files (`.jsonl` for programmatic parsing)
  * **Seq HTTP Server** (Real-time dashboard streaming)

## Installation

1. Download the repository.
2. Copy the `addons/godot_advanced_logger` directory into your project's `addons/` folder.
4. Build your C# solution (`Project -> Tools -> C# -> Build`).
5. Navigate to **Project Settings -> Plugins** and enable "Godot Advanced Logger".
6. Test the plugin with the examples found in the `addons/godot_advanced_logger/examples` directory.

*Note: This plugin requires a Godot project initialized with C# (.NET) support.*

## Configuration

Once enabled, you can configure the logger visually. 
Go to **Project Settings -> General -> Addons -> Godot Advanced Logger**.
Here you can:
* Set the minimum Log Level for the Editor (e.g., Debug, Info).
* Set a separate minimum Log Level for Release builds (e.g., Warning, Error).
* Mute noisy channels.
* Enable/Disable specific Writers (Console, File, JSON, Seq, In-Game UI).
* Configure the In-Game UI toggle hotkey (defaults to `F12`).

## Usage Guide

### Basic vs. Structured Logging

The traditional way of logging forces you to bake variables into a string. This is slow and hard to search later.

```cs
// BAD: Allocates memory even if Debug is disabled, hard to search later
LogManager.Debug($"Player {playerName} took {damage} damage from {enemy}.");
```

**The Advanced Logger Way:** Separate the *message* from the *data*.

```cs
// GOOD: Clean message, searchable data, zero allocations if Info is muted
LogManager.Info("Player Damaged", new Dictionary<string, object> 
{
    { "PlayerName", playerName },
    { "Damage", damage },
    { "EnemyType", enemy }
});
```

### Context Loggers (Best Practice)

For larger projects, create dedicated Loggers for your systems by inheriting from `ContextLogger`. This encapsulates logic and channels.

```cs
using Godot;
using System.Collections.Generic;
using GodotAdvancedLogger; // Adjust namespace to match your project

public class CombatLogger : ContextLogger
{
    // Tag all logs from this class with the "Combat" channel
    public CombatLogger() : base("Combat") { }

    public void LogAttack(string attacker, string target, int damage, bool isCritical)
    {
        // 1. Performance Gate: Abort immediately if this level is muted!
        LogLevel level = isCritical ? LogLevel.Warning : LogLevel.Info;
        if (!IsEnabled(level)) return;

        // 2. Build structured data
        var context = new Dictionary<string, object>
        {
            { "Attacker", attacker },
            { "Target", target },
            { "Damage", damage },
            { "IsCritical", isCritical }
        };

        // 3. Log
        if (isCritical) Warning("Critical Hit Executed", context);
        else Info("Attack Executed", context);
    }
}
```

### Real-Time Analytics with Seq (Highly Recommended)

This plugin includes native support for **Seq**, a free, local log server that acts as a real-time database for your game.

1. Start Seq locally via Docker:

```cmd
docker run \
  --name seq \
  -d \
  --restart unless-stopped \
  -e ACCEPT_EULA=Y \
  -e SEQ_FIRSTRUN_ADMINPASSWORD=<password> \
  -v <local path to store data>:/data \
  -p 5341:80 \
  datalust/seq
```

2. In Godot, go to **Project Settings -> Addons -> Godot Advanced Logger** and check **Enable Seq**.
3. Run your game and open `http://localhost:5341` in your browser.
4. Watch your logs stream in live. Use the search bar to query your game data instantly: 
   *(e.g., `Channel = 'Combat' and Damage > 50`)*

*Note: Replace the placeholders `<password>` and `<local path to store data>` with your local settings.*

### In-Game Debug Screen & Custom Panels

The plugin ships with a built-in `DebugScreen` Autoload. By default, pressing `F12` opens a side-panel displaying your real-time logs with channel and level filtering.

You can easily add your own debug tabs (e.g., a "Performance" or "Entity Inspector" tab) by implementing the `IDebugPanel` interface:

```csharp
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

public partial class PerformanceDebugPanel : IDebugPanel
{
    private Label _fpsLabel;

    // 1. The name displayed on the UI tab
    public string PanelName => "Performance";

    // 2. Build your custom UI using standard Godot Controls
    public Control GetPanelUi()
    {
        var container = new VBoxContainer();

        var title = new Label { Text = "Game Performance Metrics" };
        container.AddChild(title);
        
        container.AddChild(new HSeparator());

        _fpsLabel = new Label { Text = "FPS: Calculating..." };
        container.AddChild(_fpsLabel);

        return container;
    }

    // 3. Update logic (called every frame ONLY while this specific tab is visible)
    public void OnPanelUpdate(float delta)
    {
        if (_fpsLabel != null)
        {
            _fpsLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}";
        }
    }

    // Optional lifecycle hooks
    public void OnPanelShow() 
    { 
        GD.Print("Performance panel opened."); 
    }
    
    public void OnPanelHide() { }
}
```

To make your panel appear in the game, simply register it once anywhere in your code (for example, in the `_Ready` method of your main scene or a custom autoload):

```csharp
if (DebugScreen.Instance != null)
{
    DebugScreen.Instance.RegisterDebugPanel(new PerformanceDebugPanel());
}
```

## License

This project is licensed under the MIT License.
