using GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

/// <summary>
/// Log Writer to send Logs to the DebugScreen. Enabled by default, can be disabled via the plugin settings!
/// </summary>
public class DebugScreenWriter : ILogWriter
{
    public void Initialize() { }
    public void Shutdown() { }

    public void Write(in core.LogEntry entry)
    {
        if (DebugScreen.Instance != null)
        {
            DebugScreen.Instance.EnqueueLog(entry);
        }
    }
}

