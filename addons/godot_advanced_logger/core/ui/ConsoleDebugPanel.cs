using System;
using System.Collections.Generic;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Built-in "Console" panel: a command prompt for commands registered with
/// <see cref="DebugConsole"/>.
/// </summary>
/// <remarks>
/// Supports quoted arguments, command history with Up/Down, and Tab completion.
/// </remarks>
public class ConsoleDebugPanel : IDebugPanel
{
    private const int MaxHistoryEntries = 100;

    /// <summary>Colour of the echoed command line.</summary>
    private const string PromptColor = "#7fd1ff";

    /// <summary>Colour of a failed command's result.</summary>
    private const string ErrorColor = "#ff8a7a";

    private const string HintColor = "gray";

    private RichTextLabel _output;
    private LineEdit _input;

    /// <summary>Whether anything has been printed, so the blank separator is not emitted first.</summary>
    private bool _hasOutput;

    private readonly List<string> _history = new();

    /// <summary>Index into <see cref="_history"/>; equal to its count means "editing a new line".</summary>
    private int _historyIndex;

    public string PanelName => "Console";

    public ConsoleDebugPanel()
    {
        // 'clear' belongs to the panel rather than to DebugConsole.RegisterBuiltIns,
        // because it acts on this panel's output buffer.
        DebugConsole.Register("clear", _ =>
        {
            ClearOutput();
            return null;
        }, "Clears the console output");
    }

    public void OnPanelHide() { }
    public void OnPanelUpdate(float delta) { }

    public void OnPanelShow()
    {
        // Deferred: the panel's Control has only just been made visible, and GrabFocus
        // does nothing on a Control that is not yet drawable.
        _input?.CallDeferred("grab_focus");
    }

    public Control GetPanelUi()
    {
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        var monoFont = new SystemFont { FontNames = ["Consolas", "Courier New", "Monospace"] };

        _output = new RichTextLabel
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = true
        };
        _output.AddThemeFontOverride("normal_font", monoFont);
        _output.AddThemeFontOverride("bold_font", monoFont);

        root.AddChild(_output);

        var inputRow = new HBoxContainer();

        inputRow.AddChild(new Label { Text = ">" });

        _input = new LineEdit
        {
            PlaceholderText = "Type a command, or 'help'. Tab completes, Up/Down recalls history.",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _input.AddThemeFontOverride("font", monoFont);
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputGui;
        inputRow.AddChild(_input);

        var runButton = new Button { Text = "Run" };
        runButton.Pressed += () => OnSubmitted(_input.Text);
        inputRow.AddChild(runButton);

        root.AddChild(inputRow);

        PrintLine($"[color={HintColor}]Godot Advanced Logger console. Type 'help' for available commands.[/color]", raw: true);

        return root;
    }

    private void OnSubmitted(string text)
    {
        if (_input == null) return;

        _input.Clear();

        if (string.IsNullOrWhiteSpace(text)) return;

        Echo(text);
        RememberInHistory(text);

        string result = DebugConsole.Execute(text, out bool isError);

        if (!string.IsNullOrEmpty(result))
        {
            PrintLine(isError
                    ? $"[color={ErrorColor}]{Bbcode.Escape(result)}[/color]"
                    : Bbcode.Escape(result),
                raw: true);
        }

        _input.CallDeferred("grab_focus");
    }

    private void OnInputGui(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key || _input == null) return;

        switch (key.Keycode)
        {
            case Key.Up:
                NavigateHistory(-1);
                _input.AcceptEvent();
                break;

            case Key.Down:
                NavigateHistory(1);
                _input.AcceptEvent();
                break;

            case Key.Tab:
                CompleteCurrentInput();
                _input.AcceptEvent();
                break;
        }
    }

    private void RememberInHistory(string text)
    {
        // Skip consecutive duplicates so holding Up walks distinct commands.
        if (_history.Count == 0 || _history[^1] != text)
        {
            _history.Add(text);
        }

        if (_history.Count > MaxHistoryEntries) _history.RemoveAt(0);

        _historyIndex = _history.Count;
    }

    private void NavigateHistory(int direction)
    {
        if (_history.Count == 0) return;

        int index = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        _historyIndex = index;

        string text = index >= _history.Count ? string.Empty : _history[index];

        _input.Text = text;
        _input.CaretColumn = text.Length;
    }

    private void CompleteCurrentInput()
    {
        string text = _input.Text ?? string.Empty;

        // Only the command word is completed; argument completion would need per-command
        // knowledge the registry does not carry.
        if (text.Contains(' ')) return;

        var matches = DebugConsole.CompletionsFor(text);

        switch (matches.Count)
        {
            case 0:
                return;

            case 1:
                _input.Text = matches[0] + " ";
                _input.CaretColumn = _input.Text.Length;
                return;

            default:
                string common = LongestCommonPrefix(matches);
                if (common.Length > text.Length)
                {
                    _input.Text = common;
                    _input.CaretColumn = common.Length;
                }

                PrintLine($"[color={HintColor}]{Bbcode.Escape(string.Join("   ", matches))}[/color]", raw: true);
                return;
        }
    }

    private static string LongestCommonPrefix(List<string> values)
    {
        if (values.Count == 0) return string.Empty;

        string prefix = values[0];

        foreach (string value in values)
        {
            int length = 0;
            while (length < prefix.Length && length < value.Length &&
                   char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(value[length]))
            {
                length++;
            }

            prefix = prefix[..length];
            if (prefix.Length == 0) break;
        }

        return prefix;
    }

    /// <summary>
    /// Prints the entered command, preceded by a blank line so each command and its result
    /// read as one block rather than as an undifferentiated stream of text.
    /// </summary>
    private void Echo(string command)
    {
        if (_hasOutput) PrintLine(string.Empty, raw: true);

        PrintLine($"[color={PromptColor}][b]>[/b] {Bbcode.Escape(command)}[/color]", raw: true);
    }

    /// <param name="raw">
    /// When <c>true</c> the text is inserted as-is (it already contains trusted markup).
    /// Anything originating from a command handler is escaped instead.
    /// </param>
    public void PrintLine(string text, bool raw = false)
    {
        if (_output == null || text == null) return;

        _output.AppendText((raw ? text : Bbcode.Escape(text)) + "\n");
        _hasOutput = true;
    }

    public void ClearOutput()
    {
        _output?.Clear();
        _hasOutput = false;
    }

    /// <summary>Runs a command line as if it had been typed into the prompt.</summary>
    public void Run(string commandLine) => OnSubmitted(commandLine);
}
