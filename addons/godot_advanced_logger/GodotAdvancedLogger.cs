#if TOOLS
using Godot;

[Tool]
public partial class GodotAdvancedLogger : EditorPlugin
{
    private const string AUTOLOAD_NAME = "LogManager";
    private const string AUTOLOAD_PATH = "res://addons/godot_advanced_logger/core/LogManager.cs";
    
    private const string SETTING_MUTED_CHANNELS = "addons/godot_advanced_logger/settings/muted_channels";
    private const string SETTING_LOG_LEVEL = "addons/godot_advanced_logger/settings/min_log_level";
    
    private const string SETTING_ENABLE_CONSOLE = "addons/godot_advanced_logger/writers/enable_console";
    private const string SETTING_ENABLE_FILE = "addons/godot_advanced_logger/writers/enable_file";
    private const string SETTING_ENABLE_JSON = "addons/godot_advanced_logger/writers/enable_json";
    private const string SETTING_ENABLE_SEQ = "addons/godot_advanced_logger/writers/enable_seq";
    
    private const string SETTING_SEQ_URL = "addons/godot_advanced_logger/seq/server_url";
    private const string SETTING_SEQ_API_KEY = "addons/godot_advanced_logger/seq/api_key";

    public override void _EnterTree()
    {
        if (!FileAccess.FileExists(AUTOLOAD_PATH))
        {
            GD.PrintErr($"GodotAdvancedLogger: Autoload file not found at {AUTOLOAD_PATH}.");
            return;
        }
        AddAutoloadSingleton(AUTOLOAD_NAME, AUTOLOAD_PATH);
        
        // Globale Settings
        AddCustomProjectSetting(SETTING_MUTED_CHANNELS, "", Variant.Type.String, PropertyHint.None, "Comma separated list (e.g. Physics, AI)");
        AddCustomProjectSetting(SETTING_LOG_LEVEL, 0, Variant.Type.Int, PropertyHint.Enum, "Debug,Info,Warning,Error");
        
        // Writer Toggles 
        AddCustomProjectSetting(SETTING_ENABLE_CONSOLE, true, Variant.Type.Bool);
        AddCustomProjectSetting(SETTING_ENABLE_FILE, false, Variant.Type.Bool);
        AddCustomProjectSetting(SETTING_ENABLE_JSON, false, Variant.Type.Bool);
        AddCustomProjectSetting(SETTING_ENABLE_SEQ, false, Variant.Type.Bool);
        
        // Seq Config
        AddCustomProjectSetting(SETTING_SEQ_URL, "http://localhost:5341", Variant.Type.String);
        AddCustomProjectSetting(SETTING_SEQ_API_KEY, "", Variant.Type.String, PropertyHint.Password, "Optional");
        
        ProjectSettings.Save();
        GD.Print($"{AUTOLOAD_NAME} loaded and settings registered.");
    }

    public override void _ExitTree()
    {
        RemoveAutoloadSingleton(AUTOLOAD_NAME);
        GD.Print($"{AUTOLOAD_NAME} unloaded.");
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