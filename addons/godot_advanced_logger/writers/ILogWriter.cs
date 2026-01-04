using Godot;
using System;

public interface ILogWriter
{
    void Initialize();
    
    void Write(LogEntry entry);
    
    void Shutdown();
}