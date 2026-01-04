using Godot;
using System;

public record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Channel,
    string Message,
    #nullable enable
    Exception? Exception = null
    #nullable restore
);
