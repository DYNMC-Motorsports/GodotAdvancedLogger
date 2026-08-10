using System;
using System.Collections.Generic;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.examples.combat;

public partial class CombatExampleScene : Node
{
    private readonly CombatLogger _combatLog = new CombatLogger();
    private readonly core.ContextLogger _uiLog = new core.ContextLogger("UI");

    private readonly int _hp = 100; 

    public override void _Ready()
    {
        GD.Print("--- Starting Combat Logger Example ---");
        
        _combatLog.LogAttack("Hero", "Slime", 15);
        _combatLog.LogAttack("Hero", "Boss", 999, isCritical: true);
        _combatLog.LogAttack("Skeleton", "Hero", 0);
        _combatLog.LogHeal("Cleric", "Hero", 50);
        
        DebugScreen.Instance?.Watch("Hero HP", () => _hp);
        
        _uiLog.Info("Combat Window opened.", new Dictionary<string, object> 
        {
            { "WindowId", "UI_Combat_01" },
            { "Resolution", "1920x1080" }
        });
        
        GD.Print("--- End of Combat Logger Example ---");
    }
}