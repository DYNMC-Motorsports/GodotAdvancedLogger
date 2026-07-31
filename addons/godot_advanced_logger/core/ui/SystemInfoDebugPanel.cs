using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Built-in "System" panel: the environment details you would otherwise have to ask a
/// tester for by hand - engine build, OS, CPU, GPU, renderer, window size and session id.
/// </summary>
public class SystemInfoDebugPanel : IDebugPanel
{
    private GridContainer _grid;
    private Label _statusLabel;

    private readonly List<KeyValuePair<string, string>> _rows = new();

    public string PanelName => "System";

    public void OnPanelHide() { }
    public void OnPanelUpdate(float delta) { }

    /// <summary>Values such as window size can change while the game runs, so re-collect on open.</summary>
    public void OnPanelShow() => Refresh();

    public Control GetPanelUi()
    {
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        var toolbar = new HBoxContainer();

        var copyButton = new Button { Text = "Copy", TooltipText = "Copy these details to the clipboard" };
        copyButton.Pressed += CopyToClipboard;
        toolbar.AddChild(copyButton);

        var refreshButton = new Button { Text = "Refresh" };
        refreshButton.Pressed += Refresh;
        toolbar.AddChild(refreshButton);

        toolbar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        root.AddChild(toolbar);
        root.AddChild(new HSeparator());

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,

            // Values here are long (GPU names, user data paths); they wrap rather than
            // widening the panel, so a horizontal scrollbar is never wanted.
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };

        _grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        scroll.AddChild(_grid);
        root.AddChild(scroll);

        _statusLabel = new Label { Text = string.Empty, ClipText = true };
        root.AddChild(_statusLabel);

        Refresh();

        return root;
    }

    public void Refresh()
    {
        Collect();

        if (_grid == null) return;

        foreach (var child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }

        var monoFont = new SystemFont { FontNames = ["Consolas", "Courier New", "Monospace"] };

        foreach (var row in _rows)
        {
            _grid.AddChild(new Label { Text = "  " + row.Key });

            var value = new Label
            {
                Text = row.Value,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            value.AddThemeFontOverride("font", monoFont);

            _grid.AddChild(value);
        }
    }

    /// <remarks>
    /// Every lookup is individually guarded: these APIs vary by platform and headless
    /// builds, and one unavailable value must not blank out the whole panel.
    /// </remarks>
    private void Collect()
    {
        _rows.Clear();

        Add("Game", Setting("application/config/name", "(unnamed)"));
        Add("Game version", Setting("application/config/version", "(unset)"));
        Add("Engine", Safe(() =>
        {
            var info = Engine.GetVersionInfo();
            return info.TryGetValue("string", out var value) ? value.AsString() : Engine.GetVersionInfo().ToString();
        }));
        Add("Build", OS.IsDebugBuild() ? "debug" : "release");
        Add("Session", LogManager.SessionId);

        Add("OS", Safe(() => $"{OS.GetName()} {OS.GetVersion()}".Trim()));
        Add("CPU", Safe(() => $"{OS.GetProcessorName()} ({OS.GetProcessorCount()} threads)"));
        Add("Locale", Safe(OS.GetLocale));

        Add("GPU", Safe(RenderingServer.GetVideoAdapterName));
        Add("GPU vendor", Safe(RenderingServer.GetVideoAdapterVendor));
        Add("Graphics API", Safe(RenderingServer.GetVideoAdapterApiVersion));
        Add("Renderer", Setting("rendering/renderer/rendering_method", "(unknown)"));

        Add("Display server", Safe(DisplayServer.GetName));
        Add("Window size", Safe(() => Describe(DisplayServer.WindowGetSize())));
        Add("Screen size", Safe(() => Describe(DisplayServer.ScreenGetSize())));

        Add("User data dir", Safe(OS.GetUserDataDir));
    }

    private static string Describe(Vector2I size) => $"{size.X} x {size.Y}";

    private void Add(string label, string value) =>
        _rows.Add(new KeyValuePair<string, string>(label, string.IsNullOrEmpty(value) ? "(unavailable)" : value));

    private static string Safe(Func<string> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception e)
        {
            return $"(unavailable: {e.Message})";
        }
    }

    private static string Setting(string path, string fallback)
    {
        try
        {
            if (!ProjectSettings.HasSetting(path)) return fallback;

            string value = ProjectSettings.GetSetting(path).AsString();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch (Exception e)
        {
            return $"(unavailable: {e.Message})";
        }
    }

    /// <summary>Builds a plain-text block suitable for pasting into a bug report.</summary>
    public string BuildReport()
    {
        if (_rows.Count == 0) Collect();

        int width = 0;
        foreach (var row in _rows)
        {
            if (row.Key.Length > width) width = row.Key.Length;
        }

        var sb = new StringBuilder();
        foreach (var row in _rows)
        {
            sb.Append(row.Key.PadRight(width + 2)).AppendLine(row.Value);
        }

        return sb.ToString();
    }

    public void CopyToClipboard()
    {
        DisplayServer.ClipboardSet(BuildReport());

        if (_statusLabel != null) _statusLabel.Text = "  Copied system details to the clipboard.";
    }
}
