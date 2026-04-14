using Godot;
using System;
using System.Collections.Generic;

public partial class LogManager : Node
{
    public static LogManager Instance { get; private set; }

    private readonly List<ILogWriter> _writers = new();
    private readonly HashSet<string> _mutedChannels = new(StringComparer.OrdinalIgnoreCase);
    
    private LogLevel _minLogLevel = LogLevel.Info;
    
    private const string SettingMutedChannels = "addons/godot_advanced_logger/settings/muted_channels";
    private const string SettingLogLevel = "addons/godot_advanced_logger/settings/min_log_level";

    public bool IsGlobalEnabled { get; set; } = true;

    public override void _EnterTree()
    {
        if (Instance != null)
        {
            QueueFree();
            return;
        }
        Instance = this;
        
        AddWriter(new ConsoleWriter());
        AddWriter(new FileWriter());
        
        LoadSettings();
    }
    
    private void LoadSettings()
    {
        if (ProjectSettings.HasSetting(SettingMutedChannels))
        {
            string rawString = ProjectSettings.GetSetting(SettingMutedChannels).AsString();
            if (!string.IsNullOrWhiteSpace(rawString))
            {
                var channels = rawString.Split(',');
                foreach (var channel in channels)
                {
                    MuteChannel(channel.Trim());
                }
                GD.Print($"[LogManager] Loaded muted channels: {string.Join(", ", _mutedChannels)}");
            }
        }

        if (ProjectSettings.HasSetting(SettingLogLevel))
        {
            int levelInt = ProjectSettings.GetSetting(SettingLogLevel).AsInt32();
            if (Enum.IsDefined(typeof(LogLevel), levelInt))
            {
                _minLogLevel = (LogLevel)levelInt;
            }
        }
    }

    public override void _ExitTree()
    {
        Shutdown();
    }

    public void AddWriter(ILogWriter writer)
    {
        writer.Initialize();
        _writers.Add(writer);
    }

    public void MuteChannel(string channel) => _mutedChannels.Add(channel);
    public void UnmuteChannel(string channel) => _mutedChannels.Remove(channel);
    
    public static bool IsLevelEnabled(LogLevel level, string channel)
    {
        if (Instance == null || !Instance.IsGlobalEnabled) return false;
        if (level < Instance._minLogLevel) return false;
        if (Instance._mutedChannels.Contains(channel)) return false;
        
        return true;
    }

    #nullable enable
    public static void Log(LogLevel level, string channel, string message, Dictionary<string, object>? context = null, Exception? ex = null)
    {
        if (!IsLevelEnabled(level, channel)) return;

        var entry = new LogEntry(DateTime.Now, level, channel, message, context, ex);

        foreach (var writer in Instance._writers)
        {
            writer.Write(in entry);
        }
    }
    #nullable restore

    private void Shutdown()
    {
        foreach (var writer in _writers)
        {
            writer.Shutdown();
        }
        _writers.Clear(); 
    }
}