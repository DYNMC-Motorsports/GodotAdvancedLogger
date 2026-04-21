namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

public interface ILogWriter
{
    void Initialize();
    void Write(in core.LogEntry entry);
    void Shutdown();
}