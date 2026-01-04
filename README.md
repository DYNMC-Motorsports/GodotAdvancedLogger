# Godot Advanced Logger

Godot Advanced Logger is a robust and extensible C# logging framework designed specifically for Godot 4.x.

This plugin replaces standard console printing with a structured, channel-based system. It supports multiple simultaneous output targets, including rich console output and persistent file logging, while providing a flexible "Context Logger" architecture to encapsulate logging logic within specific game subsystems.

## Description

The framework is built around a centralized Singleton architecture that ensures logs are captured reliably from any thread. It creates a standardized logging format that assists in debugging complex systems by separating log generation from log consumption. Key capabilities include:

* **Centralized Management:** A static `LogManager` accessible globally.
* **Multi-Target Output:** Simultaneous writing to the Godot Console (using rich text formatting) and local log files.
* **Context-Aware Logging:** An inheritance-based system allowing for specialized loggers (e.g., `CombatLogger`) that automatically handle channel tagging and formatting logic.
* **Crash Safety:** Robust exception handling ensuring critical errors and stack traces are flushed to disk even during application instability.

## Installation

1.  Download the repository.
2.  Copy the `addons/godot_advanced_logger` directory into your project's `addons/` folder.
3.  Build your C# solution (Project -> Tools -> C# -> Build).
4.  Navigate to **Project Settings** -> **Plugins** and enable "Godot Advanced Logger".

**Note:** This plugin requires a Godot project initialized with C# (.NET) support.

## Usage Guide

Upon enabling the plugin, the `LogManager` singleton is initialized automatically. It can be accessed directly from any script in your project.

### Basic Logging

You can log Info, Warning, Error and Debug messages globally using the static methods provided by the manager.

```csharp
using Godot;
using System;

public partial class GameBootstrapper : Node
{
    public override void _Ready()
    {
        // Log simple informational messages
        LogManager.Info("System", "Game engine initialized successfully.");

        // Log warnings for non-critical issues
        LogManager.Warning("ResourceLoader", "Texture memory is at 85% capacity.");

        // Log errors with optional Exception handling
        try 
        {
            // Simulate a connection error
            throw new TimeoutException("Connection timed out.");
        }
        catch (Exception ex)
        {
            // The logger captures the stack trace automatically
            LogManager.Error("Network", "Failed to connect to master server.", ex);
        }
    }
}
```

## Advanced Architecture: Context Loggers

To maintain clean code in larger projects, it is recommended to avoid manually typing channel names (e.g., "Combat", "UI") for every log entry. Instead, this framework utilizes **Context Loggers**.

A Context Logger is a wrapper class that encapsulates the channel identity. This allows developers to implement domain-specific logging methods (such as `LogDamage` or `LogTransaction`) that handle formatting internally.

### 1. Creating a Custom Logger

Inherit from the `ContextLogger` class to create a specialized logger. This enables the encapsulation of business logic within the logging layer.

```csharp
using System;

// 1. Inherit from ContextLogger
public class CombatLogger : ContextLogger
{
    // 2. Pass the fixed channel name "Combat" to the base constructor
    public CombatLogger() : base("Combat") { }

    // 3. Add semantic methods that handle formatting logic
    public void LogAttack(string attacker, string target, int damage, bool isCritical)
    {
        string message = $"{attacker} attacks {target} for {damage} DMG";

        if (isCritical)
        {
            // Escalate critical hits to Warnings for better visibility
            Warning($"{message} (CRITICAL HIT!)");
        }
        else if (damage == 0)
        {
            // Downgrade misses to Debug logs to avoid clutter
            Debug($"{attacker} missed {target}.");
        }
        else
        {
            Info(message);
        }
    }
}
```

### 2. Implementing the Custom Logger

Instantiate the custom logger within your game scripts (e.g., Player controller, Game Manager).

```csharp
using Godot;

public partial class Player : Node
{
    // Instantiate the custom logger
    private readonly CombatLogger _combatLog = new CombatLogger();

    public void TakeDamage(int damage)
    {
        // Usage is clean and readable
        _combatLog.LogAttack("Skeleton_Warrior", "Player_1", damage, isCritical: true);
    }
}
```

## File Output System

Logs are automatically serialized and written to the user data directory to facilitate post-execution analysis.

* **Directory:** `user://logs/godotadvancedlogger`
* **File Naming:** `session_[date].log`
* **Format:** `Timestamp | Level | Channel | Message`

To access these files, select **Project** -> **Open User Data Folder** within the Godot Editor.

## Configuration

Configuration is currently handled via the `LogManager.cs` file. Future updates may expose these settings via Project Settings.
* Enable/Disable global logging.
* Mute specific channels.
* Modify timestamp formats.

## License

This project is licensed under the MIT License.
