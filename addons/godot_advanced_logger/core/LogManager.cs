using Godot;
using System;
using System.Collections.Generic;

public partial class LogManager : Node
    {
        // Singleton Instance
        public static LogManager Instance { get; private set; }

        private readonly List<ILogWriter> _writers = new();
        private readonly HashSet<string> _mutedChannels = new(StringComparer.OrdinalIgnoreCase);
        
        private LogLevel _minLogLevel = LogLevel.Info;
        
        private const string SettingMutedChannels = "addons/godot_advanced_logger/settings/muted_channels";
        private const string SettingLogLevel = "addons/godot_advanced_logger/settings/min_log_level";

        // Configuration: Global Enable/Disable
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
            // Load Muted Channels
            if (ProjectSettings.HasSetting(SettingMutedChannels))
            {
                string rawString = ProjectSettings.GetSetting(SettingMutedChannels).AsString();
                if (!string.IsNullOrWhiteSpace(rawString))
                {
                    // Split by comma, trim whitespace
                    var channels = rawString.Split(',');
                    foreach (var channel in channels)
                    {
                        MuteChannel(channel.Trim());
                    }
                
                    // Log strictly to console (bootstrap log) to verify it works
                    GD.Print($"[LogManager] Loaded muted channels: {string.Join(", ", _mutedChannels)}");
                }
            }

            // Load Minimum Log Level
            if (ProjectSettings.HasSetting(SettingLogLevel))
            {
                int levelInt = ProjectSettings.GetSetting(SettingLogLevel).AsInt32();
                // Cast int to Enum safely
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

        /// <summary>
        /// Registers a new writer (Console, File, or Custom).
        /// </summary>
        public void AddWriter(ILogWriter writer)
        {
            writer.Initialize();
            _writers.Add(writer);
        }

        /// <summary>
        /// Mutes a specific channel (e.g., "PHYSICS").
        /// </summary>
        public void MuteChannel(string channel) => _mutedChannels.Add(channel);

        /// <summary>
        /// Unmutes a specific channel.
        /// </summary>
        public void UnmuteChannel(string channel) => _mutedChannels.Remove(channel);
        
        /// <summary>
        /// Logs a simple info message.
        /// </summary>
        public static void Info(string channel, string message)
        {
            Log(LogLevel.Info, channel, message);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void Warning(string channel, string message)
        {
            Log(LogLevel.Warning, channel, message);
        }

        /// <summary>
        /// Logs and error message, optionally with an exception.
        /// </summary>
        #nullable enable
        public static void Error(string channel, string message, Exception? ex = null)
        {
            Log(LogLevel.Error, channel, message, ex);
        }
        #nullable restore
        
        public static void Debug(string channel, string message)
        {
            Log(LogLevel.Debug, channel, message);
        }

        /// <summary>
        /// Main logging method.
        /// </summary>
        #nullable enable
        public static void Log(LogLevel level, string channel, string message, Exception? ex = null)
        {
            if (Instance == null || !Instance.IsGlobalEnabled) return;
            
            if (level < Instance._minLogLevel) return;

            // Check if channel is muted
            if (Instance._mutedChannels.Contains(channel)) return;

            var entry = new LogEntry(DateTime.Now, level, channel, message, ex);

            // Distribute to all writers
            foreach (var writer in Instance._writers)
            {
                writer.Write(entry);
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
