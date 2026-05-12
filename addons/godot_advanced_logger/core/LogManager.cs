using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

public partial class LogManager : Node
{
    public static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    
    public static LogManager Instance { get; private set; }

    private readonly List<writers.ILogWriter> _writers = new();
    private readonly HashSet<string> _mutedChannels = new(StringComparer.OrdinalIgnoreCase);

    private LogLevel _minLogLevel = LogLevel.Debug;
    
    private const string SettingMutedChannels = "addons/godot_advanced_logger/settings/muted_channels";
    private const string SettingLogLevel = "addons/godot_advanced_logger/settings/min_log_level";
    private const string SettingLogLevelRelease = "addons/godot_advanced_logger/settings/min_log_level_release";

    public bool IsGlobalEnabled { get; set; } = true;

    public override void _EnterTree()
    {
        if (Instance != null)
        {
            QueueFree();
            return;
        }
        Instance = this;
        
        LoadSettings();
        InitializeWriters();
    }

    private void InitializeWriters()
    {
        bool GetBoolSetting(string path, bool def) => 
            ProjectSettings.HasSetting(path) ? ProjectSettings.GetSetting(path).AsBool() : def;
        
        bool isDebugBuild = OS.IsDebugBuild();
        
        if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_console", true))
        {
            AddWriter(new writers.ConsoleWriter());
        }
        
        if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_file", false))
        {
            AddWriter(new writers.FileWriter());
        }
        
        if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_json", false))
        {
            AddWriter(new writers.JsonFileWriter());
        }

        if (isDebugBuild)
        {
            if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_seq", false))
            {
                string seqUrl = ProjectSettings.HasSetting("addons/godot_advanced_logger/seq/server_url") 
                    ? ProjectSettings.GetSetting("addons/godot_advanced_logger/seq/server_url").AsString() 
                    : "http://localhost:5341";
                
                string seqKey = ProjectSettings.HasSetting("addons/godot_advanced_logger/seq/api_key") 
                    ? ProjectSettings.GetSetting("addons/godot_advanced_logger/seq/api_key").AsString() 
                    : null;

                AddWriter(new writers.SeqHttpWriter(seqUrl, seqKey));
            }
            
            if (GetBoolSetting("addons/godot_advanced_logger/writers/enable_overlay", true))
            {
                AddWriter(new writers.DebugScreenWriter());
            }
        } 
        else
        {
            GD.Print("[GodotAdvancedLogger] Release Build detected. Dev-Tools (Seq, InGameUI) are hard-disabled.");
        }
    }
    
    private void LoadSettings()
    {
        bool isDebug = OS.IsDebugBuild();
        
        string activeSettingPath = isDebug ? SettingLogLevel : SettingLogLevelRelease;
        
        if (!ProjectSettings.HasSetting(activeSettingPath))
        {
            activeSettingPath = SettingLogLevel;
        }
        
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

        if (ProjectSettings.HasSetting(activeSettingPath))
        {
            int levelInt = ProjectSettings.GetSetting(activeSettingPath).AsInt32();
            if (Enum.IsDefined(typeof(LogLevel), levelInt))
            {
                _minLogLevel = (LogLevel)levelInt;
                
                GD.Print($"[LogManager] Configured for {(isDebug ? "Debug" : "Release")}. Min Level: {_minLogLevel}");
            }
        }
    }

    public override void _ExitTree()
    {
        Shutdown();
    }

    public void AddWriter(writers.ILogWriter writer)
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
    public static void Log(
        LogLevel level, 
        string channel,
        string message,
        [CallerFilePath] string callerFilePath = "",
        [CallerMemberName] string callerMemberName = "",
        [CallerLineNumber] int callerLineNumber = 0,
        Dictionary<string, object>? context = null, 
        Exception? ex = null)
    {
        if (!IsLevelEnabled(level, channel)) return;

        var entry = new LogEntry(
            DateTime.Now, 
            level, channel, 
            message, 
            callerFilePath, 
            callerMemberName, 
            callerLineNumber, 
            context, 
            ex);

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