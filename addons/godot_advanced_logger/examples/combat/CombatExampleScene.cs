using Godot;
using System;

public partial class CombatExampleScene : Node
{
    // Instantiate special combat logger
    private readonly CombatLogger _combatLog = new CombatLogger();

    // Comparison: General UI logger
    private readonly ContextLogger _uiLog = new ContextLogger("UI");

    public override void _Ready()
    {
        GD.Print("--- Starting Combat Logger Example ---");

        // Different scenarios:
        _combatLog.LogAttack("Hero", "Slime", 15);
        _combatLog.LogAttack("Hero", "Boss", 999, isCritical: true);
        _combatLog.LogAttack("Skeleton", "Hero", 0);
        _combatLog.LogHeal("Cleric", "Hero", 50);

        // General UI log
        _uiLog.Info("Combat Window opened.");
        
        GD.Print("--- End of Combat Logger Example ---");
    }
}
