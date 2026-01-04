#if TOOLS
using Godot;
using System;

[Tool]
public partial class GodotAdvancedLogger : EditorPlugin
{
	private const string AutoloadName = "LogManager";
	private const string AutoloadPath = "res://addons/godot_advanced_logger/core/LogManager.cs";
	
	private const string SettingMutedChannels = "addons/godot_advanced_logger/settings/muted_channels";
	private const string SettingLogLevel = "addons/godot_advanced_logger/settings/min_log_level";

	public override void _EnterTree()
	{
		if (!FileAccess.FileExists(AutoloadPath))
		{
			GD.PrintErr($"GodotAdvancedLogger: Autoload file not found at {AutoloadPath}.");
			return;
		}
		AddAutoloadSingleton(AutoloadName, AutoloadPath);
		
		AddCustomProjectSetting(SettingMutedChannels, "", Variant.Type.String, PropertyHint.None, "Comma separated list (e.g. Physics, AI)");
		AddCustomProjectSetting(SettingLogLevel, 0, Variant.Type.Int, PropertyHint.Enum, "Info,Warning,Error,Debug");
		
		ProjectSettings.Save();
        
		GD.Print($"{AutoloadName} loaded and settings registered.");
	}

	public override void _ExitTree()
	{
		RemoveAutoloadSingleton(AutoloadName);
		GD.Print($"{AutoloadName} unloaded.");
	}
	
	/// <summary>
	/// Helper method to add a setting safely.
	/// </summary>
	private void AddCustomProjectSetting(string name, Variant defaultValue, Variant.Type type, PropertyHint hint = PropertyHint.None, string hintString = "")
	{
		if (!ProjectSettings.HasSetting(name))
		{
			ProjectSettings.SetSetting(name, defaultValue);
		}

		// We must set the property info every time the plugin loads to ensure the Editor UI knows how to display it.
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
