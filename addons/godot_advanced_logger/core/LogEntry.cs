using System;
using System.Collections.Generic;

public readonly record struct LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Channel,
    string Message,
#nullable enable
    Dictionary<string, object>? ContextData = null,
    Exception? Exception = null
#nullable restore
);