using System.Collections.Generic;

public class CombatLogger : ContextLogger
{
    public CombatLogger() : base("Combat") { }

    public void LogAttack(string attacker, string target, int damage, bool isCritical = false)
    {
        LogLevel level = isCritical ? LogLevel.Warning : (damage == 0 ? LogLevel.Debug : LogLevel.Info);
        
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
            Warning($"{attacker} critically hits {target} for {damage} DMG!", context);
        }
        else if (damage == 0)
        {
            Debug($"{attacker} missed {target}!", context);
        }
        else
        {
            Info($"{attacker} attacks {target} for {damage} DMG.", context);
        }
    }

    public void LogHeal(string healer, string target, int amount)
    {
        if (!IsEnabled(LogLevel.Info)) return;

        Info($"{healer} heals {target} for {amount} HP.", new Dictionary<string, object>
        {
            { "Healer", healer },
            { "Target", target },
            { "HealAmount", amount }
        });
    }
}