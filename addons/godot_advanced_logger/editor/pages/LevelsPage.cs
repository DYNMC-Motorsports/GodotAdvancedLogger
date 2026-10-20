#if TOOLS
using System;
using System.Linq;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// The two minimum levels, side by side, with a live "ladder" showing which levels survive.
/// </summary>
internal sealed class LevelsPage : SettingsPage
{
    public const string PageId = "levels";

    public override string Id => PageId;
    public override string Title => "Log Levels";
    public override string Subtitle => "How much detail is kept while you develop, and how much ships with your game.";
    public override string IconName => "AnimationFilter";

    protected override void BuildContent(VBoxContainer root)
    {
        root.AddChild(Ui.Rich(
            "Every entry has a level. Entries [b]below[/b] the minimum are dropped before any output sees them, " +
            "so they cost almost nothing. You set the minimum twice, because what helps while developing is " +
            "rarely what you want in a shipped game."));

        BuildLevelCard(root, LoggerSettings.MinLogLevel, "Editor & debug builds", "Debug",
            "Applies when you press [b]Run[/b] in the editor, and in exports made with " +
            "[i]Export With Debug[/i] checked. Keep this low: Debug is usually what you want here.");

        BuildLevelCard(root, LoggerSettings.MinLogLevelRelease, "Release exports", "Release",
            "Applies to exports made with [i]Export With Debug[/i] unchecked, i.e. what players run. " +
            "[b]Info[/b] or [b]Warning[/b] keeps files small while still capturing what you need for bug reports.");

        var notes = Ui.Section(root, "Good to know");

        var stripping = Ui.Card(out var strippingBody, Ui.Accent);
        strippingBody.AddChild(Ui.Heading("Debug entries do not exist in release exports", 1.0f));
        strippingBody.AddChild(Ui.Rich(
            "[code]LogManager.Debug(...)[/code] and [code]ContextLogger.Debug(...)[/code] carry " +
            "[code][Conditional(\"DEBUG\")][/code], so the compiler removes the call, including its arguments, from release builds. " +
            "[code]GodotLogger.debug(...)[/code] returns immediately when [code]OS.is_debug_build()[/code] is false. " +
            "A release minimum of [b]Debug[/b] therefore behaves exactly like [b]Info[/b].", subtle: true));
        notes.AddChild(stripping);

        var runtime = Ui.Card(out var runtimeBody);
        runtimeBody.AddChild(Ui.Heading("Changing the level while the game runs", 1.0f));
        runtimeBody.AddChild(Ui.Rich(
            "These settings are read once at startup. To change the level on the fly, use " +
            "[code]LogManager.Instance.MinLogLevel[/code] from C#, [code]GodotLogger.set_min_level()[/code] from GDScript, " +
            "or type [code]loglevel warning[/code] into the in-game console.", subtle: true));
        notes.AddChild(runtime);
    }

    private void BuildLevelCard(VBoxContainer root, SettingDefinition setting, string title, string tag, string description)
    {
        var card = Ui.Card(out var body);

        var header = Ui.HBox(10);
        header.AddChild(Ui.Heading(title, 1.12f));
        header.AddChild(Ui.Badge(tag, tag == "Release" ? Ui.Warning : Ui.Accent));
        header.AddChild(EditorUi.Spacer());
        header.AddChild(RevertButton(setting));
        body.AddChild(header);

        body.AddChild(Ui.Rich(description, subtle: true));

        var pickerRow = Ui.HBox(16);
        pickerRow.AddChild(LevelPicker(setting, debugStripped: setting == LoggerSettings.MinLogLevelRelease));
        body.AddChild(pickerRow);

        body.AddChild(BuildLadder(setting));
        root.AddChild(card);
    }

    /// <summary>Spells out what the picker shows: which levels survive and which are dropped.</summary>
    private Control BuildLadder(SettingDefinition setting)
    {
        var sentence = Ui.Rich("", subtle: true);
        bool release = setting == LoggerSettings.MinLogLevelRelease;

        Bind(() =>
        {
            var min = (LogLevel)Store.GetInt(setting);
            var all = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToList();
            var kept = all.Where(l => l >= min && !(release && l == LogLevel.Debug)).ToList();
            var dropped = all.Except(kept).ToList();

            string text = dropped.Count == 0
                ? "Everything is kept."
                : $"Keeps [b]{string.Join(", ", kept)}[/b]; drops {string.Join(", ", dropped)}.";

            if (release && min == LogLevel.Debug)
            {
                text += " [color={warning}]Debug is compiled out of release builds, so this is the same as Info.[/color]";
            }

            Ui.SetRichText(sentence, text);
        });

        return sentence;
    }
}
#endif
