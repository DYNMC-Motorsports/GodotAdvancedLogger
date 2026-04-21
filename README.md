# Godot Advanced Logger (C#)

Godot Advanced Logger is a robust, high-performance, and structured logging framework designed specifically for Godot 4.x C# projects. 

Moving beyond standard `GD.Print` console outputs, this plugin introduces a standard logging architecture. It is built from the ground up to prevent garbage collector spikes (Zero-Allocation), support structured data (JSON), and stream logs in real-time to external dashboards like Seq.

## Key Features

* **Zero-Allocation Architecture:** Uses `readonly record struct` and pre-evaluation checks (`IsEnabled`) to ensure disabled logs consume **zero** string formatting or memory allocation, preventing GC stutters.
* **Structured Logging:** Stop parsing messy text strings. Send exact data points (e.g., `attackDamage = 150`) via C# Dictionaries, making your logs fully searchable and filterable in external tools like Seq.
* **Multi-Target Output:** Write simultaneously to:
  * Godot Console (with Rich Text BBCode colors)
  * Text Files (`.txt` for quick reading)
  * JSON Lines Files (`.jsonl` for programmatic parsing)
  * **Seq HTTP Server** (Real-time dashboard streaming)
* **Godot UI Integration:** Toggle writers and configure API keys directly within Godot's Project Settings. No code changes required.

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
* Set the minimum Log Level (e.g., Info, Warning).
* Mute noisy channels.
* Enable/Disable specific Writers (Console, File, JSON, Seq).
* Set your Seq Server URL.

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

## License

This project is licensed under the MIT License.
