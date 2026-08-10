# Godot Advanced Logger (C# & GDScript)

Godot Advanced Logger is a robust, high-performance, and structured logging framework for Godot 4.x .NET projects, usable from both C# and GDScript. 

Moving beyond standard `GD.Print` console outputs, this plugin introduces a standard logging architecture. It is built from the ground up to prevent garbage collector spikes (Zero-Allocation), support structured data (JSON), stream logs in real-time to external dashboards like Seq, and provide a fully extensible **In-Game Debug UI**.

## Key Features

* **Zero-Cost Debugging:** `Debug()` calls utilize C#'s `[Conditional("DEBUG")]` attribute. In your final Release builds, the compiler completely strips these calls, ensuring **zero** CPU overhead and memory allocation.
* **In-Game Debug Screen:** A toggleable overlay with five built-in tabs: Logs, Console, Watch, Performance and System. Readable directly inside your standalone game exports. Fully extensible via the `IDebugPanel` API to add your own custom developer tools.
* **In-Game Console:** Register commands from anywhere with `DebugConsole.Register(...)` and run them at a prompt, with history, Tab completion and quoted arguments.
* **GDScript Support:** A `GodotLogger` facade exposes logging, channel muting, console commands and the Watch panel to GDScript, with caller file and line resolved from the GDScript stack.
* **Production Safe:** Automatically detects Release exports (`OS.IsDebugBuild()`) and hard-disables developer tools (like the UI and network writers) while intelligently throttling log verbosity based on separate Editor/Release settings.
* **Structured Logging:** Stop parsing messy text strings. Send exact data points via C# Dictionaries, making your logs fully searchable and filterable in external tools.
* **Editor Integration:** Warnings and errors are reported to Godot's error stream, so they show up in **Debugger --> Errors** with a stack trace instead of scrolling past in Output.
* **Multi-Target Output:** Write simultaneously to:
  * Godot Console (with Rich Text BBCode colors)
  * In-Game UI Overlay
  * Text Files (`.txt` for quick reading)
  * JSON Lines Files (`.jsonl` for programmatic parsing)
  * **Seq HTTP Server** (Real-time dashboard streaming)

## Installation

1. Download the repository.
2. Copy the `addons/godot_advanced_logger` directory into your project's `addons/` folder.
3. Build your C# solution (`Project -> Tools -> C# -> Build`).
4. Navigate to **Project Settings -> Plugins** and enable "Godot Advanced Logger".
5. Test the plugin with the examples found in the `addons/godot_advanced_logger/examples` directory.

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
* **IMPORTANT:** If using GDScript, make sure the `_BRIDGE_SCRIPT` path is correctly set to `addons/godot_advanced_logger/core/GodotLogger.cs` (or your custom path) for proper GDScript integration.

## Usage Guide

All types live under `GodotAdvancedLogger.addons.godot_advanced_logger.core`, so start with:

```cs
using GodotAdvancedLogger.addons.godot_advanced_logger.core;
```

### Basic vs. Structured Logging

The traditional way of logging forces you to bake variables into a string. This is slow and hard to search later.

```cs
// BAD: builds a throwaway string on every call, and the values are
// welded into text you later have to grep or regex back out.
LogManager.Info($"Player {playerName} took {damage} damage from {enemy}.");
```

**The Advanced Logger Way:** Separate the *message* from the *data*.

```cs
// GOOD: stable message, searchable data, and nothing is built at all
// if Info is below the minimum level or the channel is muted.
LogManager.Info("Player Damaged", new Dictionary<string, object>
{
    { "PlayerName", playerName },
    { "Damage", damage },
    { "EnemyType", enemy }
}, channel: "Combat");
```

`LogManager` exposes four static entry points. All of them take the context dictionary
second, and pick up the calling file, member and line automatically:

```cs
LogManager.Debug("Cache warm started");                       // stripped entirely in release builds
LogManager.Info("Level loaded", new() { { "Level", 3 } });
LogManager.Warning("Frame budget exceeded", channel: "Perf");
LogManager.Error("Save failed", ex, channel: "IO");
```

Entries with no explicit `channel` land on the `General` channel.

> Migrating from 2.0.x: `LogManager.Log(...)` still works but is marked `[Obsolete]`.
> Its `[Caller*]` parameters sit *before* `context`, which makes `context` impossible to
> pass positionally. Use `LogManager.Write(...)` or the helpers above instead.

### Context Loggers (Best Practice)

For larger projects, create dedicated Loggers for your systems by inheriting from `ContextLogger`. This encapsulates logic and channels.

```cs
using Godot;
using System.Collections.Generic;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

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

### Using the plugin from GDScript

Everything above works from GDScript too, through the `GodotLogger` facade. It is a
`class_name` script, so there is nothing to preload and no extra autoload to configure.

```gdscript
GodotLogger.info("Player damaged", {"damage": 12, "enemy": "Slime"}, "Combat")
GodotLogger.warning("Frame budget exceeded", {"fps": Engine.get_frames_per_second()}, "Perf")
GodotLogger.error("Save failed", {"path": path}, "IO")
```

Entries land on the same channels, pass the same filters and reach the same writers,
console, files, the in-game overlay and Seq, as entries logged from C#.

> **This does not remove the .NET requirement.** The core of the plugin is C#, so your
> project must still be a Godot .NET (Mono) build with the C# solution compiled. GDScript
> support means you can *use* the logger from GDScript, not that it runs on standard Godot.

#### Caller information

The C# API picks up the calling file and line from compiler attributes, which GDScript has
no equivalent for. The facade uses `get_stack()` instead, so entries carry the same caller
information, only with one caveat: `get_stack()` returns nothing outside a debug session, so in
release exports the caller fields are left blank rather than guessed at.

#### Performance

A GDScript log call is heavier than the C# one: it crosses the language boundary, allocates
a `Dictionary`, and walks the stack. Two things to know:

* `GodotLogger.debug(...)` **returns immediately in release builds.** C# strips `Debug()`
  calls at compile time via `[Conditional("DEBUG")]`; GDScript cannot be stripped, so the
  facade checks `OS.is_debug_build()` instead. Use `info()` for entries that must survive
  into a release build.
* For hot paths, gate on `is_enabled()` so the context dictionary is never built:

```gdscript
if GodotLogger.is_enabled(GodotLogger.Level.DEBUG, "Physics"):
    GodotLogger.debug("Contacts", {"bodies": _collect_bodies()})
```

#### Console commands and watches

```gdscript
GodotLogger.register_command("give_gold", func(args):
    if args.is_empty():
        return "usage: give_gold <amount>"
    Player.gold += int(args[0])
    return "Gave %s gold." % args[0],
    "Adds gold to the player", "give_gold <amount>")

GodotLogger.watch("Player HP", func(): return _player.health)
```

The handler receives one `Array` of string arguments and returns the text to print.

#### Full API

| Function | Purpose |
|---|---|
| `debug` / `info` / `warning` / `error` `(message, context := {}, channel := "General")` | Log an entry |
| `is_enabled(level, channel := "General")` | Level gate for hot paths |
| `is_available()` | Whether the C# logger is reachable |
| `get_min_level()` / `set_min_level(level)` | Read or change the minimum level at runtime |
| `mute_channel` / `unmute_channel` / `is_channel_muted` | Channel control |
| `register_command(name, handler, description := "", usage := "")` | Add an in-game console command |
| `watch(label, provider)` / `unwatch(label)` | Add or remove a Watch panel row |

A runnable example lives in `addons/godot_advanced_logger/examples/gdscript/`.

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

### In-Game Debug Screen

The plugin ships with a `DebugScreen` autoload. Press `F12` (configurable) to open a tabbed
overlay with five built-in panels:

| Tab | What it does |
|---|---|
| **Logs** | Live log view with search, level/channel filters, Clear, Copy and Save-to-file |
| **Console** | Command prompt for commands you register yourself |
| **Watch** | Live table of values you register with one line of code |
| **Performance** | FPS, frame timing, memory, draw calls, node counts, frame-time sparkline |
| **System** | Engine / OS / CPU / GPU / renderer details, with a Copy button for bug reports |

Use **Wide** in the header to expand the overlay from the right 40% to the full viewport.

The overlay is disabled in release exports unless you set `ingame_ui_allow_in_release`, and
it can pause the game while open via `ingame_ui_pause_game`.

#### Watch a value

```csharp
DebugScreen.Instance?.Watch("Player HP", () => _player.Health);
DebugScreen.Instance?.Watch("Enemies",   () => _enemies.Count);
```

Providers are polled about ten times a second while the Watch tab is visible. One that
throws shows the exception inline instead of taking down the overlay.

#### Register a console command

`DebugConsole` is static, so you can register from anywhere at any time, it does not care
whether the overlay exists yet.

```csharp
DebugConsole.Register("give_gold", args =>
{
    if (args.Length < 1 || !int.TryParse(args[0], out int amount))
        return "usage: give_gold <amount>";

    Player.Gold += amount;
    return $"Gave {amount} gold.";
}, "Adds gold to the player", "give_gold <amount>");
```

Tab completes command names, Up/Down walk the history, and quoted arguments
(`say "hello world"`) are parsed as one argument. Built-ins include `help`, `clear`,
`loglevel`, `mute`/`unmute`, `channels`, `panels`/`panel`, `logtest` and `quit`.

#### Add your own tab

Implement `IDebugPanel`, a plain C# class, not a `Node` and not `partial`:

```csharp
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

public class EntityInspectorPanel : IDebugPanel
{
    private Label _infoLabel;

    // 1. The name displayed on the UI tab. Must be unique.
    public string PanelName => "Entities";

    // 2. Build your UI using standard Godot Controls. Called once, at registration.
    public Control GetPanelUi()
    {
        var container = new VBoxContainer();
        container.AddChild(new Label { Text = "Entity Inspector" });
        container.AddChild(new HSeparator());

        _infoLabel = new Label { Text = "Waiting for data..." };
        container.AddChild(_infoLabel);

        return container;
    }

    // 3. Called every frame, but ONLY while this tab is active and the overlay is open.
    public void OnPanelUpdate(float delta)
    {
        _infoLabel.Text = $"Tracked entities: {EntityManager.Count}";
    }

    // Optional lifecycle hooks
    public void OnPanelShow() { }
    public void OnPanelHide() { }
}
```

Register it once, for example in the `_Ready` of your main scene:

```csharp
DebugScreen.Instance?.RegisterDebugPanel(new EntityInspectorPanel());
```

And remove it again when the owning scene unloads:

```csharp
DebugScreen.Instance?.UnregisterDebugPanel("Entities");
```

See [DEBUGSCREEN_GUIDE.md](addons/godot_advanced_logger/DEBUGSCREEN_GUIDE.md) for the full
reference.

## Log files

`FileWriter` and `JsonFileWriter` write to `user://logs/godotadvancedlogger`, one file per
run, named `log_<timestamp>.txt` / `.jsonl`. Each writer keeps its 50 most recent files and
deletes the rest on startup.

## License

This project is licensed under the MIT License.
