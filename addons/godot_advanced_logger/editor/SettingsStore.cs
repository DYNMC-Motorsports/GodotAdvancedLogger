#if TOOLS
using System;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// Reads and writes the logger's Project Settings for the settings window.
/// </summary>
/// <remarks>
/// <para>
/// <c>project.godot</c> stays the only storage: the window is a nicer view on the same
/// values the stock Project Settings dialog edits. Changes are applied to
/// <see cref="ProjectSettings"/> immediately and written to disk after a short debounce
/// (driven by <see cref="Tick"/>), so typing into a field does not rewrite the file on
/// every keystroke.
/// </para>
/// <para>
/// Deliberately a plain C# object with no engine signal connections of its own: anything
/// an engine object holds a delegate to would dangle after a C# assembly reload. The
/// window owns the <c>settings_changed</c> connection and forwards it via
/// <see cref="NotifyExternalChange"/>.
/// </para>
/// </remarks>
internal sealed class SettingsStore
{
    private const ulong SaveDelayMs = 400;

    private bool _savePending;
    private ulong _saveDueAt;

    /// <summary>Raised after any logger setting may have changed, here or elsewhere.</summary>
    public event Action Changed;

    public bool GetBool(SettingDefinition setting) => LoggerSettings.GetBool(setting);
    public int GetInt(SettingDefinition setting) => LoggerSettings.GetInt(setting);
    public string GetString(SettingDefinition setting) => LoggerSettings.GetString(setting);

    public void SetBool(SettingDefinition setting, bool value)
    {
        if (GetBool(setting) == value) return;
        Apply(setting, Variant.CreateFrom(value));
    }

    public void SetInt(SettingDefinition setting, int value)
    {
        if (GetInt(setting) == value) return;
        Apply(setting, Variant.CreateFrom(value));
    }

    public void SetString(SettingDefinition setting, string value)
    {
        value ??= string.Empty;
        if (GetString(setting) == value) return;
        Apply(setting, Variant.CreateFrom(value));
    }

    /// <summary>Whether the setting currently holds its schema default.</summary>
    public bool IsDefault(SettingDefinition setting) => setting.DefaultValue switch
    {
        bool b => GetBool(setting) == b,
        int i => GetInt(setting) == i,
        string s => GetString(setting) == s,
        _ => true
    };

    public void ResetToDefault(SettingDefinition setting)
    {
        switch (setting.DefaultValue)
        {
            case bool b: SetBool(setting, b); break;
            case int i: SetInt(setting, i); break;
            case string s: SetString(setting, s); break;
        }
    }

    /// <summary>Call regularly (every frame is fine): writes pending changes once the debounce has elapsed.</summary>
    public void Tick()
    {
        if (_savePending && Time.GetTicksMsec() >= _saveDueAt) Flush();
    }

    /// <summary>Writes pending changes to <c>project.godot</c> now.</summary>
    public void Flush()
    {
        if (!_savePending) return;
        _savePending = false;

        var error = ProjectSettings.Save();
        if (error != Godot.Error.Ok)
        {
            GD.PushError($"[GodotAdvancedLogger] Could not save project settings: {error}");
        }
    }

    /// <summary>Forwards ProjectSettings' <c>settings_changed</c> signal.</summary>
    public void NotifyExternalChange() => Changed?.Invoke();

    private void Apply(SettingDefinition setting, Variant value)
    {
        ProjectSettings.SetSetting(setting.Path, value);

        _savePending = true;
        _saveDueAt = Time.GetTicksMsec() + SaveDelayMs;

        Changed?.Invoke();
    }
}
#endif
