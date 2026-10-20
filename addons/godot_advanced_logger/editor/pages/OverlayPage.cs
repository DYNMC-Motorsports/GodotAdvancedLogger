#if TOOLS
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// The in-game debug overlay: hotkey (captured by pressing it), pause behaviour and the
/// release opt-in.
/// </summary>
internal sealed class OverlayPage : SettingsPage
{
    public const string PageId = "overlay";

    public override string Id => PageId;
    public override string Title => "In-Game Overlay";
    public override string Subtitle => "The tabbed debug screen you open inside the running game.";
    public override string IconName => "CanvasLayer";

    private Button _capture;
    private Label _hotkeyNote;
    private bool _capturing;

    private PanelContainer _releaseCard;

    protected override void BuildContent(VBoxContainer root)
    {
        root.AddChild(Ui.Rich(
            "The overlay is an autoload with five tabs: [b]Logs[/b], [b]Console[/b] (your registered commands), " +
            "[b]Watch[/b] (live values), [b]Performance[/b] and [b]System[/b]. You can add your own tabs with " +
            "[code]DebugScreen.Instance.RegisterDebugPanel(...)[/code]."));

        BuildHotkeySection(root);

        var behaviour = Ui.Section(root, "Behaviour");
        var card = Ui.Card(out var body);
        ToggleRow(body, LoggerSettings.OverlayPauseGame,
            "Pauses the SceneTree while the overlay is open, so you can inspect a frozen moment. " +
            "The overlay keeps running; nodes with [code]ProcessMode = Always[/code] do too.");
        body.AddChild(new HSeparator());
        ToggleRow(body, LoggerSettings.EnableOverlay, "Show log entries in the Logs tab",
            "Streams log entries into the [b]Logs[/b] tab. Switch this off to keep the overlay for the console and " +
            "watches only.");
        behaviour.AddChild(card);

        BuildReleaseSection(root);
    }

    // ---------------------------------------------------------------------
    // Hotkey
    // ---------------------------------------------------------------------

    private void BuildHotkeySection(VBoxContainer root)
    {
        var section = Ui.Section(root, "Hotkey");
        var card = Ui.Card(out var body);

        var row = Ui.HBox(12);
        var text = Ui.VBox(2);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(Ui.Heading(LoggerSettings.OverlayHotkey.Label, 1.0f));
        text.AddChild(Ui.Rich("Click the button, then press the key you want. [b]Esc[/b] cancels.", subtle: true));
        row.AddChild(text);

        row.AddChild(RevertButton(LoggerSettings.OverlayHotkey));

        _capture = new Button
        {
            ToggleMode = true,
            CustomMinimumSize = new Vector2(Ui.S(160), Ui.S(34)),
            FocusMode = Control.FocusModeEnum.All,
            Icon = Ui.Icon("Keyboard"),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (Ui.BoldFont != null) _capture.AddThemeFontOverride("font", Ui.BoldFont);
        _capture.Toggled += on =>
        {
            _capturing = on;
            if (on) _capture.GrabFocus();
            RefreshCaptureButton();
        };
        _capture.GuiInput += OnCaptureInput;
        _capture.FocusExited += () =>
        {
            if (!_capturing) return;
            _capturing = false;
            _capture.SetPressedNoSignal(false);
            RefreshCaptureButton();
        };
        row.AddChild(_capture);
        body.AddChild(row);

        _hotkeyNote = Ui.Text("", subtle: true, wrap: true);
        body.AddChild(_hotkeyNote);

        section.AddChild(card);

        Bind(RefreshCaptureButton);
    }

    private void OnCaptureInput(InputEvent @event)
    {
        if (!_capturing) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        // Swallow the event first, so neither the button nor editor shortcuts react to it.
        _capture.AcceptEvent();

        var keycode = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;

        if (keycode == Key.Escape)
        {
            StopCapture();
            return;
        }

        if (IsModifier(keycode)) return; // wait for the actual key

        Store.SetString(LoggerSettings.OverlayHotkey, keycode.ToString());
        StopCapture();
    }

    private void StopCapture()
    {
        _capturing = false;
        _capture.SetPressedNoSignal(false);
        RefreshCaptureButton();
    }

    private void RefreshCaptureButton()
    {
        if (_capture == null) return;

        if (_capturing)
        {
            _capture.Text = "Press a key…";
            _capture.AddThemeColorOverride("font_color", Ui.Accent);
            _hotkeyNote.Text = "Modifier keys are ignored: the overlay reacts to the key alone.";
            _hotkeyNote.AddThemeColorOverride("font_color", Ui.Subtle);
            return;
        }

        _capture.RemoveThemeColorOverride("font_color");

        string stored = Store.GetString(LoggerSettings.OverlayHotkey);
        if (LoggerSettings.TryParseHotkey(stored, out var key))
        {
            _capture.Text = OS.GetKeycodeString(key);

            if (IsTypingKey(key))
            {
                _hotkeyNote.Text = "This key is ignored while a text field in the overlay has focus (so you can type it), " +
                                   "and your game may use it too. A function key such as F12 avoids both.";
                _hotkeyNote.AddThemeColorOverride("font_color", Ui.Warning);
            }
            else
            {
                _hotkeyNote.Text = $"Stored as \"{stored}\". The overlay toggles whenever this key is pressed in a debug build.";
                _hotkeyNote.AddThemeColorOverride("font_color", Ui.Subtle);
            }
        }
        else
        {
            _capture.Text = "F12 (fallback)";
            _hotkeyNote.Text = $"\"{stored}\" is not a valid key name, so the overlay uses F12. Click the button to pick a key.";
            _hotkeyNote.AddThemeColorOverride("font_color", Ui.Error);
        }
    }

    private static bool IsModifier(Key key) =>
        key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta or Key.Capslock;

    /// <summary>Keys that produce text: the overlay ignores them while one of its text fields has focus.</summary>
    internal static bool IsTypingKey(Key key) =>
        key is >= Key.Space and <= Key.Asciitilde;

    // ---------------------------------------------------------------------
    // Release
    // ---------------------------------------------------------------------

    private void BuildReleaseSection(VBoxContainer root)
    {
        var section = Ui.Section(root, "Release exports");
        _releaseCard = Ui.Card(out var body);

        ToggleRow(body, LoggerSettings.OverlayAllowInRelease,
            "By default the overlay only exists in debug builds. Turning this on makes it available to players, " +
            "[color={warning}]including every console command you registered[/color]. Useful for closed playtests; " +
            "think twice before shipping it.");

        section.AddChild(_releaseCard);

        Bind(() =>
        {
            bool on = Store.GetBool(LoggerSettings.OverlayAllowInRelease);
            var style = Ui.Box(on ? Ui.CardBackground.Lerp(Ui.Warning, 0.07f) : Ui.CardBackground,
                on ? new Color(Ui.Warning, 0.55f) : Ui.CardBorder, 6, 14);
            if (on)
            {
                style.BorderWidthLeft = Ui.S(3);
                style.BorderColor = Ui.Warning;
            }
            _releaseCard.AddThemeStyleboxOverride("panel", style);
        });
    }
}
#endif
