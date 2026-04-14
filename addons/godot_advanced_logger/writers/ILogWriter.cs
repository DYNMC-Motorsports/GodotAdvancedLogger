public interface ILogWriter
{
    void Initialize();
    void Write(in LogEntry entry);
    void Shutdown();
}