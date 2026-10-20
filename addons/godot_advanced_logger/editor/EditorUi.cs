#if TOOLS
using System;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// Theme-aware factory for the controls used by the settings window. Everything is
/// derived from the active editor theme and scale, so the window looks native in dark,
/// light and custom themes and on HiDPI displays.
/// </summary>
internal sealed class EditorUi
{
    public const string DocsUrl = "https://github.com/DYNMC-Motorsports/GodotAdvancedLogger#readme";
    public const string SeqDocsUrl = "https://datalust.co/docs";

    /// <summary>Prefix for RichTextLabel meta links that navigate inside the window, e.g. <c>[url=page:seq]</c>.</summary>
    public const string PageLinkPrefix = "page:";

    public Theme Theme { get; }
    public float Scale { get; }

    public Color Accent { get; }
    public Color Success { get; }
    public Color Warning { get; }
    public Color Error { get; }
    public Color Font { get; }
    public Color Subtle { get; }
    public Color Base { get; }
    public Color CardBackground { get; }
    public Color CardBorder { get; }
    public Color SidebarBackground { get; }

    public Font BoldFont { get; }
    public Font MonoFont { get; }
    public int FontSize { get; }

    /// <summary>Raised when a <c>page:</c> link is clicked in any rich text created by this factory.</summary>
    public event Action<string> NavigateRequested;

    public EditorUi()
    {
        Theme = EditorInterface.Singleton.GetEditorTheme();
        Scale = EditorInterface.Singleton.GetEditorScale();

        Accent = ThemeColor("accent_color", "Editor", new Color(0.44f, 0.73f, 0.98f));
        Success = ThemeColor("success_color", "Editor", new Color(0.45f, 0.95f, 0.5f));
        Warning = ThemeColor("warning_color", "Editor", new Color(1f, 0.87f, 0.4f));
        Error = ThemeColor("error_color", "Editor", new Color(1f, 0.47f, 0.42f));
        Font = ThemeColor("font_color", "Label", Colors.White);
        Base = ThemeColor("base_color", "Editor", new Color(0.21f, 0.24f, 0.29f));

        Subtle = new Color(Font, 0.62f);

        // Lerping towards the font color works for dark and light themes alike:
        // it always moves the surface a little towards "more contrast".
        CardBackground = Base.Lerp(Font, 0.045f);
        CardBorder = Base.Lerp(Font, 0.11f);
        SidebarBackground = ThemeColor("dark_color_2", "Editor", Base.Darkened(0.25f));

        BoldFont = Theme.HasFont("bold", "EditorFonts") ? Theme.GetFont("bold", "EditorFonts") : null;
        MonoFont = Theme.HasFont("source", "EditorFonts") ? Theme.GetFont("source", "EditorFonts") : null;
        FontSize = Theme.HasFontSize("main_size", "EditorFonts") ? Theme.GetFontSize("main_size", "EditorFonts") : S(14);
    }

    /// <summary>Scales a pixel value by the editor scale.</summary>
    public int S(float px) => Mathf.RoundToInt(px * Scale);

    public Texture2D Icon(string name) =>
        Theme.HasIcon(name, "EditorIcons") ? Theme.GetIcon(name, "EditorIcons") : null;

    public Color LevelColor(LogLevel level) => level switch
    {
        LogLevel.Debug => Subtle,
        LogLevel.Info => Accent,
        LogLevel.Warning => Warning,
        LogLevel.Error => Error,
        _ => Font
    };

    public Color SeverityColor(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Error => Error,
        IssueSeverity.Warning => Warning,
        _ => Accent
    };

    public Texture2D SeverityIcon(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Error => Icon("StatusError"),
        IssueSeverity.Warning => Icon("StatusWarning"),
        _ => Icon("NodeInfo") ?? Icon("Help")
    };

    // ---------------------------------------------------------------------
    // Text
    // ---------------------------------------------------------------------

    public Label Heading(string text, float sizeFactor = 1.3f)
    {
        var label = new Label { Text = text };
        if (BoldFont != null) label.AddThemeFontOverride("font", BoldFont);
        label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(FontSize * sizeFactor));
        return label;
    }

    public Label Text(string text, bool subtle = false, bool wrap = false)
    {
        var label = new Label { Text = text };
        if (subtle) label.AddThemeColorOverride("font_color", Subtle);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.CustomMinimumSize = new Vector2(S(120), 0);
        }
        return label;
    }

    public Label Mono(string text)
    {
        var label = new Label { Text = text };
        if (MonoFont != null) label.AddThemeFontOverride("font", MonoFont);
        return label;
    }

    /// <summary>
    /// A wrapping, auto-sized RichTextLabel. Supports the color tokens <c>{accent}</c>,
    /// <c>{success}</c>, <c>{warning}</c>, <c>{error}</c> and <c>{subtle}</c>, and handles
    /// <c>http(s)://</c> and <c>page:</c> links.
    /// </summary>
    public RichTextLabel Rich(string bbcode, bool subtle = false)
    {
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(S(160), 0),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

        // The editor theme gives RichTextLabel a filled "normal" box; descriptions should sit flush.
        rich.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        rich.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (BoldFont != null) rich.AddThemeFontOverride("bold_font", BoldFont);
        if (MonoFont != null) rich.AddThemeFontOverride("mono_font", MonoFont);
        if (subtle) rich.AddThemeColorOverride("default_color", Subtle);

        rich.MetaClicked += meta => OnMetaClicked(meta.AsString());
        SetRichText(rich, bbcode);
        return rich;
    }

    public void SetRichText(RichTextLabel rich, string bbcode)
    {
        string text = bbcode
            .Replace("{accent}", "#" + Accent.ToHtml(false))
            .Replace("{success}", "#" + Success.ToHtml(false))
            .Replace("{warning}", "#" + Warning.ToHtml(false))
            .Replace("{error}", "#" + Error.ToHtml(false))
            .Replace("{subtle}", "#" + Subtle.ToHtml(true));

        if (rich.Text == text) return;
        rich.Text = text;
    }

    private void OnMetaClicked(string meta)
    {
        if (meta.StartsWith(PageLinkPrefix, StringComparison.Ordinal))
        {
            NavigateRequested?.Invoke(meta[PageLinkPrefix.Length..]);
        }
        else if (meta.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || meta.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            OS.ShellOpen(meta);
        }
    }

    // ---------------------------------------------------------------------
    // Containers
    // ---------------------------------------------------------------------

    public StyleBoxFlat Box(Color background, Color? border = null, float radius = 6, float padding = 12)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            CornerDetail = 6,
        };
        box.SetCornerRadiusAll(S(radius));
        box.SetContentMarginAll(S(padding));

        if (border.HasValue)
        {
            box.BorderColor = border.Value;
            box.SetBorderWidthAll(Math.Max(1, S(1)));
        }

        return box;
    }

    /// <summary>A rounded, bordered card. Add content to <paramref name="body"/>.</summary>
    public PanelContainer Card(out VBoxContainer body, Color? stripe = null)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var style = Box(CardBackground, CardBorder, 6, 14);

        if (stripe.HasValue)
        {
            style.BorderColor = stripe.Value;
            style.BorderWidthLeft = S(3);
        }

        card.AddThemeStyleboxOverride("panel", style);

        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", S(8));
        card.AddChild(body);
        return card;
    }

    /// <summary>A section with a heading, an optional rich description and a body for rows.</summary>
    public VBoxContainer Section(Control parent, string title, string description = null)
    {
        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", S(8));

        var header = Heading(title, 1.08f);
        section.AddChild(header);

        if (!string.IsNullOrEmpty(description))
        {
            section.AddChild(Rich(description, subtle: true));
        }

        parent.AddChild(section);
        return section;
    }

    /// <summary>A small rounded pill, e.g. "Debug builds only".</summary>
    public PanelContainer Badge(string text, Color color, string tooltip = null)
    {
        var pill = new PanelContainer
        {
            TooltipText = tooltip ?? string.Empty,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = string.IsNullOrEmpty(tooltip) ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop,
        };
        var style = Box(new Color(color, 0.16f), new Color(color, 0.55f), 9, 0);
        style.ContentMarginLeft = S(8);
        style.ContentMarginRight = S(8);
        style.ContentMarginTop = S(1);
        style.ContentMarginBottom = S(1);
        pill.AddThemeStyleboxOverride("panel", style);

        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", color.Lerp(Font, 0.25f));
        label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(FontSize * 0.85f));
        pill.AddChild(label);
        return pill;
    }

    public Button IconButton(string iconName, string tooltip, string text = null)
    {
        var button = new Button
        {
            Icon = Icon(iconName),
            TooltipText = tooltip ?? string.Empty,
            Text = text ?? string.Empty,
            Flat = string.IsNullOrEmpty(text),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.Click,
        };
        return button;
    }

    public TextureRect IconRect(Texture2D texture, Color? modulate = null)
    {
        var rect = new TextureRect
        {
            Texture = texture,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(S(16), S(16)),
        };
        if (modulate.HasValue) rect.Modulate = modulate.Value;
        return rect;
    }

    public static Control Spacer(bool horizontal = true) => new Control
    {
        SizeFlagsHorizontal = horizontal ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        SizeFlagsVertical = horizontal ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public HBoxContainer HBox(int separation = 8)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", S(separation));
        return box;
    }

    public VBoxContainer VBox(int separation = 8)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", S(separation));
        return box;
    }

    /// <summary>Removes and frees every child right away, so a rebuilt list never shows stale rows for a frame.</summary>
    public static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Color ThemeColor(string name, string type, Color fallback) =>
        Theme.HasColor(name, type) ? Theme.GetColor(name, type) : fallback;
}
#endif
