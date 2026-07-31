# DebugScreen — Guide

## Overview

The `DebugScreen` is the in-game debug overlay. It is a `CanvasLayer` autoload registered by
the plugin, hidden at startup, and toggled with **F12** (or whatever key you set in Project
Settings).

The overlay is a tabbed shell. Every tab is an `IDebugPanel`. Five panels ship with the
plugin, and you can add your own.

| Tab             | What it does                                                                    |
|-----------------|---------------------------------------------------------------------------------|
| **Logs**        | Live log view with search, level and channel filters, clipboard and file export |
| **Console**     | Command prompt for commands you register with `DebugConsole`                    |
| **Watch**       | Live table of values registered with `DebugScreen.Instance.Watch(...)`          |
| **Performance** | FPS, frame timing, memory, draw calls, node counts, frame-time sparkline        |
| **System**      | Engine / OS / CPU / GPU / renderer details, with a Copy button for bug reports  |

## Shell controls

* **Wide / Narrow**: the overlay covers the right 40% of the viewport by default. *Wide*
  expands it to the full viewport, which is what you want for reading long log lines or
  wide stack traces.
* **Close**: hides the overlay. The hotkey does the same thing.
* The active tab is visibly highlighted, and Godot's `TabContainer` gives you keyboard
  navigation between tabs for free.

## Project settings

Under **Project Settings → General → Addons → Godot Advanced Logger → Settings**:

| Setting                      | Default | Effect                                             |
|------------------------------|---------|----------------------------------------------------|
| `ingame_ui_hotkey`           | `F12`   | Key that toggles the overlay. Any `Key` enum name. |
| `ingame_ui_pause_game`       | `false` | Pauses the `SceneTree` while the overlay is open.  |
| `ingame_ui_allow_in_release` | `false` | Lets the overlay open in release exports.          |

### A note on input

The overlay swallows mouse clicks over its own area, and while a text field inside it has
focus Godot's GUI consumes key events before they reach `_unhandled_input`.

What it cannot intercept is **polling**. Code calling `Input.IsActionPressed(...)` every
frame runs regardless of what has focus, so your player will still move while you type into
the console. If that bothers you, enable `ingame_ui_pause_game`. The overlay keeps updating
while paused (its `ProcessMode` is `Always`), and closing it restores whatever pause state
the game had before, so it will not stomp your own pause menu.

The hotkey is handled in `_ShortcutInput`, so it fires even when the console input has
focus. If you set the hotkey to a plain letter, it is ignored while you are typing into a
text field, otherwise you could never type that letter.

### Release builds

The overlay is a developer tool, so it is disabled in release exports unless you set
`ingame_ui_allow_in_release`. This matches how `LogManager` gates Seq and the overlay
writer. Note these are two separate switches: `writers/enable_overlay` controls whether logs
are *routed* to the overlay, `ingame_ui_allow_in_release` controls whether the overlay
*exists* at all in a release build.

## The Logs panel

* **Search**: Substring match, case-insensitive, applied on top of the level and channel
  filters. It searches the whole rendered line, so channel names and context values match too.
* **Follow**: Auto-scroll to the newest entry. Scrolling up by hand turns it off
  automatically so the view stops fighting you; click it again to jump back to the bottom.
* **Clear / Copy / Save**: Copy puts the currently visible entries on the clipboard as
  plain text. Save writes them to `user://logs/godotadvancedlogger/export_<timestamp>.txt`
  and reports the absolute path in the status line.
* **Channels**: Discovered automatically as entries arrive, listed alphabetically, with
  **All** / **None** buttons.
* History is capped at 1000 entries; the status line shows how many are shown vs buffered.

## The Console panel

Register a command from anywhere. `DebugConsole` is static and does not care whether the
overlay exists yet:

```csharp
using GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

DebugConsole.Register("give_gold", args =>
{
    if (args.Length < 1 || !int.TryParse(args[0], out int amount))
        return "usage: give_gold <amount>";

    Player.Gold += amount;
    return $"Gave {amount} gold. Player now has {Player.Gold}.";
},
description: "Adds gold to the player",
usage: "give_gold <amount>");
```

The handler receives already-parsed arguments and returns the text to print (return `null`
to print nothing). Exceptions thrown by a handler are caught and shown in the console rather
than crashing the game.

Arguments are split on whitespace, and double quotes group a single argument:
`say "hello world"` passes one argument, not two.

At the prompt: **Tab** completes command names, **Up / Down** walk the history.

### Built-in commands

| Command                               | Description                                                      |
|---------------------------------------|------------------------------------------------------------------|
| `help [command]`                      | List commands, or show details for one                           |
| `clear`                               | Clear the console output                                         |
| `quit`                                | Close the game                                                   |
| `loglevel [level]`                    | Show or set the minimum log level at runtime                     |
| `mute <channel>` / `unmute <channel>` | Silence or restore a channel                                     |
| `channels`                            | List every channel seen this session                             |
| `panels` / `panel <name>`             | List panels, or switch to one                                    |
| `logtest`                             | Emit one entry at each level, to check your writers are wired up |

## The Watch panel

For values you want to see continuously without writing a panel:

```csharp
DebugScreen.Instance?.Watch("Player HP",  () => _player.Health);
DebugScreen.Instance?.Watch("Enemies",    () => _enemies.Count);
DebugScreen.Instance?.Watch("State",      () => _stateMachine.CurrentState);
DebugScreen.Instance?.Watch("Position",   () => _player.GlobalPosition);
```

Providers are polled about ten times a second while the panel is visible, so keep them
cheap. A provider that throws — a freed node, a null field — shows the exception inline
instead of taking down the overlay.

`Unwatch(label)` removes one; `ClearWatches()` removes all. Registering the same label twice
replaces the provider rather than adding a duplicate row.

## Writing a custom panel

Implement `IDebugPanel`. Note it is a plain C# class, **not** `partial`, and not a `Node`.
You build and return a Godot `Control` from `GetPanelUi()`, and the overlay parents it.

```csharp
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

public class HoverUnitInfoPanel : IDebugPanel
{
    private Label _infoLabel;
    private Unit _hoveredUnit;

    // 1. The tab title. Must be unique.
    public string PanelName => "Unit Info";

    // 2. Build the UI. Called once, when the panel is registered.
    public Control GetPanelUi()
    {
        var container = new VBoxContainer();
        _infoLabel = new Label { Text = "Hover over a unit..." };
        container.AddChild(_infoLabel);
        return container;
    }

    // 3. Called every frame, but only while this tab is the active one AND the
    //    overlay is open. Panels that are not visible cost nothing.
    public void OnPanelUpdate(float delta)
    {
        _hoveredUnit = FindUnitAtPosition(GetGlobalMousePosition());

        _infoLabel.Text = _hoveredUnit != null
            ? $"Unit: {_hoveredUnit.Name}\nHP: {_hoveredUnit.Health}/{_hoveredUnit.MaxHealth}"
            : "Hover over a unit...";
    }

    // 4. Lifecycle hooks - good places to subscribe / unsubscribe.
    public void OnPanelShow() { }
    public void OnPanelHide() { }
}
```

Register it once, for example in your main scene's `_Ready()`:

```csharp
DebugScreen.Instance?.RegisterDebugPanel(new HoverUnitInfoPanel());
```

And remove it again when the owning scene unloads:

```csharp
DebugScreen.Instance?.UnregisterDebugPanel("Unit Info");
```

### Registration rules

* `PanelName` must be unique. Registering a name that already exists is a no-op.
* `GetPanelUi()` is called once, at registration — not every frame.
* If `GetPanelUi()` returns `null`, the tab is still created with an empty placeholder.
* Panel names may contain characters Godot forbids in node names (`. : @ / " %`); the node
  name is sanitized while the tab keeps your original text.

### Performance notes

`OnPanelUpdate` only runs for the **active** tab of an **open** overlay, so a panel that is
not being looked at costs nothing. Do expensive work there rather than in `_Process`
somewhere else.

Log entries reach the overlay through a thread-safe queue; at most 200 are moved into the UI
per frame (buffering up to 5000) so a logging burst cannot stall a frame.
