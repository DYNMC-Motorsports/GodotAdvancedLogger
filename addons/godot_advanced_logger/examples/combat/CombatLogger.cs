using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.examples.combat;

public class CombatLogger : core.ContextLogger
{
    public CombatLogger() : base("Combat") { }

    public void LogAttack(string attacker, string target, int damage, bool isCritical = false, [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
    {
        core.LogLevel level = isCritical ? core.LogLevel.Warning : (damage == 0 ? core.LogLevel.Debug : core.LogLevel.Info);
        
        if (!IsEnabled(level)) return;
        
        var context = new Dictionary<string, object>
        {
            { "Attacker", attacker },
            { "Target", target },
            { "Damage", damage },
            { "IsCritical", isCritical }
        };
        
        if (isCritical)
        {
            Warning($"{attacker} critically hits {target} for {damage} DMG!", context, file, member, line);
        }
        else if (damage == 0)
        {
            Debug($"{attacker} missed {target}!", context, file, member, line);
        }
        else
        {
            Info($"{attacker} attacks {target} for {damage} DMG.", context, file, member, line);
        }
    }

    public void LogHeal(string healer, string target, int amount, [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
    {
        if (!IsEnabled(core.LogLevel.Info)) return;

        Info($"{healer} heals {target} for {amount} HP.", new Dictionary<string, object>
        {
            { "Healer", healer },
            { "Target", target },
            { "HealAmount", amount }
        }, file, member, line);
    }
}