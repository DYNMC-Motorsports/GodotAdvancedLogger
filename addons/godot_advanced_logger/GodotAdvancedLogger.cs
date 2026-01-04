#if TOOLS
using Godot;
using System;

[Tool]
public partial class GodotAdvancedLogger : EditorPlugin
{
	private const string AutoloadName = "LogManager";
	private const string AutoloadPath = "res://addons/godot_advanced_logger/core/LogManager.cs";

	public override void _EnterTree()
	{
		AddAutoloadSingleton(AutoloadName, AutoloadPath);
		GD.Print($"{AutoloadName} loaded successfully.");
	}

	public override void _ExitTree()
	{
		RemoveAutoloadSingleton(AutoloadName);
		GD.Print($"{AutoloadName} unloaded.");
	}
}
#endif
