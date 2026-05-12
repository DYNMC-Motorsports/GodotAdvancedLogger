using System.Collections.Generic;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.examples.debug_screen;

public partial class DebugScreenExampleScene : Node
{
    // Example Loggers using different channels
    private readonly ContextLogger _combatLog = new("Combat");
    private readonly ContextLogger _physicsLog = new("Physics");
    private readonly ContextLogger _uiLog = new("UI");

    public override void _Ready()
    {
        GD.Print("--- Debug Screen Test started ---");
        GD.Print("Press 'F12' or the key you set in the settings to open up the Debug Screen.");
        GD.Print("Press 'Space' to generate some random logs from different channels.");
        GD.Print("-----------------------------------");
        
        // Initial Log Entries to show up in the Debug Screen when opening it for the first time
        _uiLog.Info("Test-Scene loaded");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Space })
        {
            GenerateRandomLogs();
        }
    }

    private void GenerateRandomLogs()
    {
        _combatLog.Info("Player attacks with 15 damage");
        _physicsLog.Debug("Detected collision with wall", new Dictionary<string, object> { {"Velocity", "15.2"} });
        _uiLog.Warning("Button 'Start' is not responding!");
        
        if (GD.Randi() % 5 == 0)
        {
            _combatLog.Error("Critical Error. Target not found!", new System.Exception("NullReferenceException: Target is null"));
        }
    }
}