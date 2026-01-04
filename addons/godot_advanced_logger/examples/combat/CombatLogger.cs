using Godot;
using System;

/// <summary>
/// Specific logger for combat-related events.
/// Extends ContextLogger to provide specialized logging methods for combat scenarios.
/// </summary>
public class CombatLogger : ContextLogger
{
    // Call of the basic constructor with the channel name "Combat".
    // All logs from this logger will be tagged with this channel.
    public CombatLogger() : base("Combat") { }

    /// <summary>
    /// Logs an attack event with custom logic
    /// </summary>
    public void LogAttack(string attacker, string target, int damage, bool isCritical = false)
    {
        // LOGIC 1: Message Formatting
        // Message is getting formatted with relevant data
        string message = $"{attacker} attacks {target} for {damage} DMG";

        if (isCritical)
        {
            message += " (CRITICAL HIT!)";
            
            // LOGIC 2: Critical Hits as Warnings
            // Critical Hits are important -> Warning
            Warning(message); 
        }
        else if (damage == 0)
        {
            // LOGIC 3: Filtering 
            // Hits, that do no damage are less important -> Debug
            Debug($"{attacker} missed {target}!");
        }
        else
        {
            // Normal hit --> Info
            Info(message);
        }
    }

    /// <summary>
    /// Logs healing.
    /// </summary>
    public void LogHeal(string healer, string target, int amount)
    {
        Info($"{healer} heals {target} for {amount} HP.");
    }
}
