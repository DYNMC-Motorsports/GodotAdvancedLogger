#if TOOLS
using System.Runtime.Loader;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;
using GodotAdvancedLogger.addons.godot_advanced_logger.editor;

namespace GodotAdvancedLogger.addons.godot_advanced_logger;

[Tool]
public partial class GodotAdvancedLogger : EditorPlugin
{
    private const string AutoloadName = "LogManager";
    private const string AutoloadPath = "res://addons/godot_advanced_logger/core/LogManager.cs";

    private const string AutoloadNameOverlay = "DebugScreen";
    private const string AutoloadPathOverlay = "res://addons/godot_advanced_logger/core/ui/DebugScreen.cs";

    private const string ToolMenuLabel = "Advanced Logger Settings...";
    private const string CommandKey = "godot_advanced_logger/open_settings";

    private LoggerSettingsWindow _settingsWindow;

    public override void _EnterTree()
    {
        if (!FileAccess.FileExists(AutoloadPath))
        {
            GD.PrintErr($"GodotAdvancedLogger: Autoload file not found at {AutoloadPath}.");
            return;
        }
        AddAutoloadSingleton(AutoloadName, AutoloadPath);
        AddAutoloadSingleton(AutoloadNameOverlay, AutoloadPathOverlay);

        foreach (var setting in LoggerSettings.All)
        {
            RegisterProjectSetting(setting);
        }

        ProjectSettings.Save();

        // Rebuilding the C# project unloads this assembly while the editor keeps running.
        var loadContext = AssemblyLoadContext.GetLoadContext(typeof(GodotAdvancedLogger).Assembly);
        if (loadContext != null) loadContext.Unloading += OnAssemblyUnloading;

        AddToolMenuItem(ToolMenuLabel, Callable.From(OpenSettingsWindow));
        EditorInterface.Singleton.GetCommandPalette()
            .AddCommand("Open Advanced Logger Settings", CommandKey, Callable.From(OpenSettingsWindow));

        GD.Print($"{AutoloadName} loaded and settings registered.");
    }

    public override void _ExitTree()
    {
        var loadContext = AssemblyLoadContext.GetLoadContext(typeof(GodotAdvancedLogger).Assembly);
        if (loadContext != null) loadContext.Unloading -= OnAssemblyUnloading;

        RemoveToolMenuItem(ToolMenuLabel);
        EditorInterface.Singleton.GetCommandPalette()?.RemoveCommand(CommandKey);

        // Free the window before the C# assembly is unloaded (plugin disabled or project rebuilt),
        // otherwise Godot keeps a node whose script no longer exists.
        if (IsInstanceValid(_settingsWindow))
        {
            _settingsWindow.QueueFree();
        }
        _settingsWindow = null;

        RemoveAutoloadSingleton(AutoloadName);
        RemoveAutoloadSingleton(AutoloadNameOverlay);
        GD.Print($"{AutoloadName} unloaded.");
    }

    /// <summary>Opens the settings window, creating it on first use.</summary>
    public void OpenSettingsWindow()
    {
        // After an assembly reload Godot restores the window node, but not its managed
        // state (pages, store). Such a husk is replaced rather than shown.
        if (IsInstanceValid(_settingsWindow) && !_settingsWindow.IsBuilt)
        {
            _settingsWindow.QueueFree();
            _settingsWindow = null;
        }

        if (!IsInstanceValid(_settingsWindow))
        {
            _settingsWindow = new LoggerSettingsWindow();
            EditorInterface.Singleton.GetBaseControl().AddChild(_settingsWindow);
        }

        _settingsWindow.Open();
    }

    private void OnAssemblyUnloading(AssemblyLoadContext context)
    {
        // Write pending edits before the managed state is thrown away. The window removes
        // itself after the reload (see LoggerSettingsWindow._Process).
        if (IsInstanceValid(_settingsWindow))
        {
            _settingsWindow.ReleaseManagedResources();
        }
    }

    private static void RegisterProjectSetting(SettingDefinition setting)
    {
        if (!ProjectSettings.HasSetting(setting.Path))
        {
            ProjectSettings.SetSetting(setting.Path, setting.DefaultVariant);
        }

        var propertyInfo = new Godot.Collections.Dictionary
        {
            { "name", setting.Path },
            { "type", (int)setting.Type },
            { "hint", (int)setting.Hint },
            { "hint_string", setting.HintString }
        };

        ProjectSettings.AddPropertyInfo(propertyInfo);
        ProjectSettings.SetInitialValue(setting.Path, setting.DefaultVariant);

        // Without this the settings only appear once "Advanced Settings" is toggled on.
        ProjectSettings.SetAsBasic(setting.Path, true);
    }
}
#endif
