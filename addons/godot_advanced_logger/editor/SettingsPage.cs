#if TOOLS
using System;
using System.Collections.Generic;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// One page of the settings window. Pages build plain Godot controls and keep a list of
/// refresh callbacks that pull the current value of every bound setting back into its
/// control, so external edits (stock Project Settings dialog, revert, one-click fixes)
/// always show up.
/// </summary>
internal abstract class SettingsPage
{
    private readonly List<Action> _refreshers = new();

    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    public abstract string IconName { get; }

    protected EditorUi Ui { get; private set; }
    protected SettingsStore Store { get; private set; }
    protected LoggerSettingsWindow Window { get; private set; }

    public Control Root { get; private set; }

    public Control Build(EditorUi ui, SettingsStore store, LoggerSettingsWindow window)
    {
        Ui = ui;
        Store = store;
        Window = window;

        var root = ui.VBox(18);
        root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Root = root;

        BuildContent(root);
        Refresh();
        return root;
    }

    protected abstract void BuildContent(VBoxContainer root);

    /// <summary>Re-reads every bound setting into its control.</summary>
    public void Refresh()
    {
        foreach (var refresh in _refreshers) refresh();
        OnRefresh();
    }

    protected virtual void OnRefresh() { }

    /// <summary>Called whenever the page becomes the visible page.</summary>
    public virtual void OnShown() { }

    protected void Bind(Action refresh) => _refreshers.Add(refresh);

    // ---------------------------------------------------------------------
    // Bound controls
    // ---------------------------------------------------------------------

    /// <summary>
    /// The small "revert" arrow the inspector shows next to a modified property. Visible only
    /// while the setting differs from its default.
    /// </summary>
    protected Button RevertButton(SettingDefinition setting)
    {
        var button = Ui.IconButton("Reload", $"Revert to default ({FormatDefault(setting)})");
        button.Pressed += () => Store.ResetToDefault(setting);
        Bind(() => button.Visible = !Store.IsDefault(setting));
        return button;
    }

    protected CheckButton Toggle(SettingDefinition setting)
    {
        var toggle = new CheckButton
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = setting.Path,
            FocusMode = Control.FocusModeEnum.All,
        };
        toggle.Toggled += pressed => Store.SetBool(setting, pressed);
        Bind(() => toggle.SetPressedNoSignal(Store.GetBool(setting)));
        return toggle;
    }

    /// <summary>
    /// A row with a title, a rich description, optional badges, a revert arrow and a switch.
    /// </summary>
    protected HBoxContainer ToggleRow(Control parent, SettingDefinition setting, string description,
        params Control[] badges) => ToggleRow(parent, setting, setting.Label, description, badges);

    protected HBoxContainer ToggleRow(Control parent, SettingDefinition setting, string title, string description,
        params Control[] badges)
    {
        var row = Ui.HBox(12);

        var text = Ui.VBox(2);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var titleRow = Ui.HBox(8);
        titleRow.AddChild(Ui.Heading(title, 1.0f));
        foreach (var badge in badges) titleRow.AddChild(badge);
        text.AddChild(titleRow);

        if (!string.IsNullOrEmpty(description))
        {
            text.AddChild(Ui.Rich(description, subtle: true));
        }

        row.AddChild(text);
        row.AddChild(RevertButton(setting));
        row.AddChild(Toggle(setting));

        parent.AddChild(row);
        return row;
    }

    /// <summary>
    /// A segmented "keep from here" control for a <see cref="LogLevel"/> setting. Clicking a
    /// level selects it as the minimum; every level that survives is tinted in its color and
    /// the dropped ones are dimmed, so the bar doubles as a picture of the filter.
    /// </summary>
    /// <param name="setting">The level setting to bind.</param>
    /// <param name="debugStripped">True for release builds, where Debug calls are compiled out and never survive.</param>
    protected HBoxContainer LevelPicker(SettingDefinition setting, bool debugStripped = false)
    {
        var box = Ui.HBox(0);
        var group = new ButtonGroup();
        var buttons = new List<(LogLevel Level, Button Button)>();

        foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
        {
            var button = new Button
            {
                Text = level.ToString(),
                ToggleMode = true,
                ButtonGroup = group,
                CustomMinimumSize = new Vector2(Ui.S(96), Ui.S(32)),
                FocusMode = Control.FocusModeEnum.All,
            };
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            if (Ui.BoldFont != null) button.AddThemeFontOverride("font", Ui.BoldFont);

            int value = (int)level;
            button.Toggled += on =>
            {
                if (on) Store.SetInt(setting, value);
            };

            buttons.Add((level, button));
            box.AddChild(button);
        }

        Bind(() =>
        {
            var min = (LogLevel)Store.GetInt(setting);

            foreach (var (level, button) in buttons)
            {
                bool selected = level == min;
                bool stripped = debugStripped && level == LogLevel.Debug;
                bool kept = level >= min && !stripped;
                var color = Ui.LevelColor(level);

                button.SetPressedNoSignal(selected);
                button.TooltipText = stripped
                    ? "Debug calls are compiled out of release builds, so Debug never reaches the logger here"
                    : kept ? $"{level} entries are kept" : $"{level} entries are dropped. Click to keep {level} and above";

                StyleBoxFlat normal, hover, pressed;
                Color font;

                if (kept)
                {
                    normal = Ui.Box(Ui.CardBackground.Lerp(color, 0.2f), new Color(color, 0.55f), 0, 6);
                    hover = Ui.Box(Ui.CardBackground.Lerp(color, 0.3f), new Color(color, 0.75f), 0, 6);
                    font = color.Lerp(Ui.Font, 0.35f);
                }
                else
                {
                    normal = Ui.Box(Ui.CardBackground, Ui.CardBorder, 0, 6);
                    hover = Ui.Box(Ui.CardBackground.Lerp(color, 0.12f), Ui.CardBorder, 0, 6);
                    font = new Color(Ui.Font, 0.4f);
                }

                pressed = Ui.Box(stripped ? Ui.CardBackground.Lerp(color, 0.35f) : color.Darkened(0.1f), color, 0, 6);
                pressed.SetBorderWidthAll(Math.Max(1, Ui.S(2)));
                var pressedFont = color.Darkened(0.1f).Luminance > 0.5f && !stripped ? new Color(0.08f, 0.08f, 0.1f) : Colors.White;

                ApplySegmentCorners(level, normal, hover, pressed);

                button.AddThemeStyleboxOverride("normal", normal);
                button.AddThemeStyleboxOverride("hover", hover);
                button.AddThemeStyleboxOverride("pressed", pressed);
                button.AddThemeStyleboxOverride("hover_pressed", pressed);
                button.AddThemeColorOverride("font_color", font);
                button.AddThemeColorOverride("font_hover_color", kept ? font.Lightened(0.15f) : color);
                button.AddThemeColorOverride("font_pressed_color", pressedFont);
                button.AddThemeColorOverride("font_hover_pressed_color", pressedFont);
            }
        });

        return box;
    }

    private void ApplySegmentCorners(LogLevel level, params StyleBoxFlat[] boxes)
    {
        int radius = Ui.S(6);
        bool first = level == LogLevel.Debug;
        bool last = level == LogLevel.Error;

        foreach (var box in boxes)
        {
            box.CornerRadiusTopLeft = box.CornerRadiusBottomLeft = first ? radius : 0;
            box.CornerRadiusTopRight = box.CornerRadiusBottomRight = last ? radius : 0;
            if (!first && box.BorderWidthLeft <= Ui.S(1)) box.BorderWidthLeft = 0;
        }
    }

    protected static string FormatDefault(SettingDefinition setting) => setting.DefaultValue switch
    {
        bool b => b ? "On" : "Off",
        int i when setting.Hint == PropertyHint.Enum => ((LogLevel)i).ToString(),
        string s when s.Length == 0 => "empty",
        var v => v.ToString()
    };
}
#endif
