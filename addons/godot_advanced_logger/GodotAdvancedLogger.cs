#if TOOLS
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger;

[Tool]
public partial class GodotAdvancedLogger : EditorPlugin
{
    private const string AutoloadName = "LogManager";
    private const string AutoloadPath = "res://addons/godot_advanced_logger/core/LogManager.cs";
    
    private const string SettingMutedChannels = "addons/godot_advanced_logger/settings/muted_channels";
    private const string SettingLogLevel = "addons/godot_advanced_logger/settings/min_log_level";
    
    private const string SettingEnableConsole = "addons/godot_advanced_logger/writers/enable_console";
    private const string SettingEnableFile = "addons/godot_advanced_logger/writers/enable_file";
    private const string SettingEnableJson = "addons/godot_advanced_logger/writers/enable_json";
    private const string SettingEnableSeq = "addons/godot_advanced_logger/writers/enable_seq";
    
    private const string SettingSeqUrl = "addons/godot_advanced_logger/seq/server_url";
    private const string SettingSeqApiKey = "addons/godot_advanced_logger/seq/api_key";

    public override void _EnterTree()
    {
        if (!FileAccess.FileExists(AutoloadPath))
        {
            GD.PrintErr($"GodotAdvancedLogger: Autoload file not found at {AutoloadPath}.");
            return;
        }
        AddAutoloadSingleton(AutoloadName, AutoloadPath);
        
        // Globale Settings
        AddCustomProjectSetting(SettingMutedChannels, "", Variant.Type.String, PropertyHint.None, "Comma separated list (e.g. Physics, AI)");
        AddCustomProjectSetting(SettingLogLevel, 0, Variant.Type.Int, PropertyHint.Enum, "Debug,Info,Warning,Error");
        
        // Writer Toggles 
        AddCustomProjectSetting(SettingEnableConsole, true, Variant.Type.Bool);
        AddCustomProjectSetting(SettingEnableFile, false, Variant.Type.Bool);
        AddCustomProjectSetting(SettingEnableJson, false, Variant.Type.Bool);
        AddCustomProjectSetting(SettingEnableSeq, false, Variant.Type.Bool);
        
        // Seq Config
        AddCustomProjectSetting(SettingSeqUrl, "http://localhost:5341", Variant.Type.String);
        AddCustomProjectSetting(SettingSeqApiKey, "", Variant.Type.String, PropertyHint.Password, "Optional");
        
        ProjectSettings.Save();
        GD.Print($"{AutoloadName} loaded and settings registered.");
    }

    public override void _ExitTree()
    {
        RemoveAutoloadSingleton(AutoloadName);
        GD.Print($"{AutoloadName} unloaded.");
    }
    
    private void AddCustomProjectSetting(string name, Variant defaultValue, Variant.Type type, PropertyHint hint = PropertyHint.None, string hintString = "")
    {
        if (!ProjectSettings.HasSetting(name))
        {
            ProjectSettings.SetSetting(name, defaultValue);
        }
        
        var propertyInfo = new Godot.Collections.Dictionary
        {
            { "name", name },
            { "type", (int)type },
            { "hint", (int)hint },
            { "hint_string", hintString }
        };

        ProjectSettings.AddPropertyInfo(propertyInfo);
        ProjectSettings.SetInitialValue(name, defaultValue);
    }
}
#endif