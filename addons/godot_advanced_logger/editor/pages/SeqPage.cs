#if TOOLS
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// Seq integration: endpoint with live validation, API key, a real connection test,
/// a Docker quick start and a reference of what is sent.
/// </summary>
internal sealed class SeqPage : SettingsPage
{
    public const string PageId = "seq";

    private const string DockerCommand =
        "docker run --name seq -d --restart unless-stopped \\\n" +
        "  -e ACCEPT_EULA=Y \\\n" +
        "  -e SEQ_FIRSTRUN_ADMINPASSWORD=<password> \\\n" +
        "  -v <local path to store data>:/data \\\n" +
        "  -p 5341:80 \\\n" +
        "  datalust/seq";

    public override string Id => PageId;
    public override string Title => "Seq";
    public override string Subtitle => "Stream structured logs to a Seq dashboard while you develop.";
    public override string IconName => "ToolConnect";

    private LineEdit _url;
    private VBoxContainer _urlIssues;
    private Label _ingestPreview;
    private LineEdit _apiKey;
    private Button _revealKey;

    private Button _testButton;
    private PanelContainer _resultCard;
    private TextureRect _resultIcon;
    private Label _resultTitle;
    private RichTextLabel _resultDetail;

    private SeqConnectionTester _tester;
    private bool _testPending;

    protected override void BuildContent(VBoxContainer root)
    {
        _tester = new SeqConnectionTester(root);
        _tester.Completed += ShowResult;

        var enableCard = Ui.Card(out var enableBody);
        ToggleRow(enableBody, LoggerSettings.EnableSeq,
            "Sends every entry to Seq in batches from a background thread. If the server is down, entries are " +
            "buffered (up to 10,000) and retried with backoff; the game never waits for the network.",
            Ui.Badge("Debug builds only", Ui.Accent, "Hard-disabled in release exports, whatever this switch says."));
        root.AddChild(enableCard);

        BuildServerSection(root);
        BuildTestSection(root);
        BuildQuickStart(root);
        BuildReference(root);
    }

    // ---------------------------------------------------------------------
    // Server
    // ---------------------------------------------------------------------

    private void BuildServerSection(VBoxContainer root)
    {
        var section = Ui.Section(root, "Server",
            "Enter the address you open Seq with in the browser. The logger adds " +
            $"[code]{SeqEndpoint.IngestPath}[/code] itself.");

        var card = Ui.Card(out var body);

        // URL
        var urlHeader = Ui.HBox(8);
        urlHeader.AddChild(Ui.Heading(LoggerSettings.SeqServerUrl.Label, 1.0f));
        urlHeader.AddChild(EditorUi.Spacer());
        urlHeader.AddChild(RevertButton(LoggerSettings.SeqServerUrl));
        body.AddChild(urlHeader);

        var urlRow = Ui.HBox(8);
        _url = new LineEdit
        {
            PlaceholderText = SeqEndpoint.DefaultBaseUrl,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClearButtonEnabled = true,
            SelectAllOnFocus = false,
        };
        if (Ui.MonoFont != null) _url.AddThemeFontOverride("font", Ui.MonoFont);
        _url.TextChanged += text =>
        {
            Store.SetString(LoggerSettings.SeqServerUrl, text);
            RefreshUrlFeedback();
        };
        urlRow.AddChild(_url);

        var open = Ui.IconButton("ExternalLink", "Open this Seq server in the browser", "Open Seq");
        open.Pressed += () => OS.ShellOpen(SeqEndpoint.Normalize(_url.Text));
        urlRow.AddChild(open);
        body.AddChild(urlRow);

        var previewRow = Ui.HBox(6);
        previewRow.AddChild(Ui.Text("Events are posted to", subtle: true));
        _ingestPreview = Ui.Mono("");
        _ingestPreview.AddThemeColorOverride("font_color", Ui.Accent);
        previewRow.AddChild(_ingestPreview);
        body.AddChild(previewRow);

        _urlIssues = Ui.VBox(6);
        body.AddChild(_urlIssues);

        Bind(() =>
        {
            string stored = Store.GetString(LoggerSettings.SeqServerUrl);
            if (_url.Text != stored)
            {
                int caret = _url.CaretColumn;
                _url.Text = stored;
                _url.CaretColumn = Mathf.Min(caret, stored.Length);
            }
            RefreshUrlFeedback();
        });

        body.AddChild(new HSeparator());

        // API key
        var keyHeader = Ui.HBox(8);
        keyHeader.AddChild(Ui.Heading(LoggerSettings.SeqApiKey.Label, 1.0f));
        keyHeader.AddChild(Ui.Badge("Optional", Ui.Subtle));
        keyHeader.AddChild(EditorUi.Spacer());
        keyHeader.AddChild(RevertButton(LoggerSettings.SeqApiKey));
        body.AddChild(keyHeader);

        body.AddChild(Ui.Rich(
            "Only needed if your Seq server requires authenticated ingestion. Create a key in Seq under " +
            "[b]Settings → API Keys[/b] with the [i]Ingest[/i] permission. It is sent as the " +
            "[code]X-Seq-ApiKey[/code] header. [color={warning}]The key is stored in project.godot, " +
            "so it ends up in version control.[/color] Use a key with ingest-only rights.", subtle: true));

        var keyRow = Ui.HBox(8);
        _apiKey = new LineEdit
        {
            Secret = true,
            PlaceholderText = "No API key",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        if (Ui.MonoFont != null) _apiKey.AddThemeFontOverride("font", Ui.MonoFont);
        _apiKey.TextChanged += text =>
        {
            Store.SetString(LoggerSettings.SeqApiKey, text.Trim());
            RefreshUrlFeedback();
        };
        keyRow.AddChild(_apiKey);

        _revealKey = Ui.IconButton("GuiVisibilityHidden", "Show the key");
        _revealKey.ToggleMode = true;
        _revealKey.Toggled += shown =>
        {
            _apiKey.Secret = !shown;
            _revealKey.Icon = Ui.Icon(shown ? "GuiVisibilityVisible" : "GuiVisibilityHidden");
            _revealKey.TooltipText = shown ? "Hide the key" : "Show the key";
        };
        keyRow.AddChild(_revealKey);
        body.AddChild(keyRow);

        Bind(() =>
        {
            string stored = Store.GetString(LoggerSettings.SeqApiKey);
            if (_apiKey.Text.Trim() != stored) _apiKey.Text = stored;
        });

        section.AddChild(card);
    }

    private void RefreshUrlFeedback()
    {
        string url = _url.Text;
        _ingestPreview.Text = SeqEndpoint.GetIngestUrl(url);

        EditorUi.ClearChildren(_urlIssues);

        var issues = SeqEndpoint.Analyze(url, hasApiKey: !string.IsNullOrEmpty(_apiKey?.Text));
        foreach (var issue in issues)
        {
            var row = Ui.HBox(8);
            var severity = issue.Severity switch
            {
                UrlIssueSeverity.Error => IssueSeverity.Error,
                UrlIssueSeverity.Warning => IssueSeverity.Warning,
                _ => IssueSeverity.Info
            };

            row.AddChild(Ui.IconRect(Ui.SeverityIcon(severity)));
            var text = Ui.Text(issue.Message, wrap: true);
            text.AddThemeColorOverride("font_color", Ui.SeverityColor(severity).Lerp(Ui.Font, 0.35f));
            row.AddChild(text);

            if (issue.SuggestedUrl != null)
            {
                var fix = new Button { Text = "Use " + issue.SuggestedUrl, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                string suggested = issue.SuggestedUrl;
                fix.Pressed += () =>
                {
                    Store.SetString(LoggerSettings.SeqServerUrl, suggested);
                    _url.Text = suggested;
                    RefreshUrlFeedback();
                };
                row.AddChild(fix);
            }

            _urlIssues.AddChild(row);
        }

        if (issues.Count == 0)
        {
            var ok = Ui.HBox(8);
            ok.AddChild(Ui.IconRect(Ui.Icon("StatusSuccess")));
            ok.AddChild(Ui.Text("The URL looks valid.", subtle: true));
            _urlIssues.AddChild(ok);
        }
    }

    // ---------------------------------------------------------------------
    // Connection test
    // ---------------------------------------------------------------------

    private void BuildTestSection(VBoxContainer root)
    {
        var section = Ui.Section(root, "Connection test",
            "Sends one test event exactly the way the game would, using the URL and key above. " +
            "Works even while the Seq output is switched off.");

        var row = Ui.HBox(10);
        _testButton = new Button { Text = "Test connection", Icon = Ui.Icon("ToolConnect"), CustomMinimumSize = new Vector2(Ui.S(170), Ui.S(32)) };
        _testButton.Pressed += StartTest;
        row.AddChild(_testButton);
        section.AddChild(row);

        _resultCard = Ui.Card(out var resultBody);
        _resultCard.Visible = false;
        var resultRow = Ui.HBox(12);
        _resultIcon = Ui.IconRect(null);
        resultRow.AddChild(_resultIcon);

        var resultText = Ui.VBox(2);
        resultText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _resultTitle = Ui.Heading("", 1.0f);
        _resultDetail = Ui.Rich("", subtle: true);
        resultText.AddChild(_resultTitle);
        resultText.AddChild(_resultDetail);
        resultRow.AddChild(resultText);
        resultBody.AddChild(resultRow);
        section.AddChild(_resultCard);
    }

    /// <summary>Starts a connection test. Safe to call before the page has been shown.</summary>
    public void StartTest()
    {
        if (_tester == null || _tester.IsRunning) return;

        // The window may have been opened from the Overview's quick action before this
        // page's controls were ever laid out; the tester only needs the stored values.
        string url = Store.GetString(LoggerSettings.SeqServerUrl);
        string key = Store.GetString(LoggerSettings.SeqApiKey);

        _testPending = true;
        _testButton.Disabled = true;
        _testButton.Text = "Testing…";

        _resultCard.Visible = true;
        StyleResult(Ui.Accent);
        _resultIcon.Texture = Ui.Icon("ToolConnect");
        _resultTitle.Text = "Contacting " + SeqEndpoint.Normalize(url) + " …";
        Ui.SetRichText(_resultDetail, "");

        _tester.Start(url, key);
    }

    private void ShowResult(SeqConnectionTester.Result result)
    {
        if (!_testPending) return;
        _testPending = false;

        _testButton.Disabled = false;
        _testButton.Text = "Test again";

        bool ok = result.Outcome == SeqConnectionTester.Outcome.Success;
        var color = ok ? Ui.Success : result.Outcome == SeqConnectionTester.Outcome.Unauthorized ? Ui.Warning : Ui.Error;

        StyleResult(color);
        _resultIcon.Texture = Ui.Icon(ok ? "StatusSuccess" : result.Outcome == SeqConnectionTester.Outcome.Unauthorized ? "StatusWarning" : "StatusError");
        _resultTitle.Text = result.Headline;

        string meta = result.StatusCode > 0
            ? $"[color={{subtle}}]HTTP {result.StatusCode} · {result.ElapsedMs:0} ms[/color]"
            : result.ElapsedMs > 0 ? $"[color={{subtle}}]{result.ElapsedMs:0} ms[/color]" : "";

        string detail = Escape(result.Detail);
        if (ok)
        {
            detail = "A test event was accepted. Find it in Seq with [code]Channel = 'GodotAdvancedLogger'[/code].";
            if (!Store.GetBool(LoggerSettings.EnableSeq))
            {
                detail += " [color={warning}]The Seq output is still switched off above.[/color]";
            }
        }

        Ui.SetRichText(_resultDetail, string.IsNullOrEmpty(meta) ? detail : detail + "\n" + meta);
    }

    private void StyleResult(Color color)
    {
        var style = Ui.Box(Ui.CardBackground.Lerp(color, 0.08f), new Color(color, 0.5f), 6, 14);
        style.BorderWidthLeft = Ui.S(3);
        style.BorderColor = color;
        _resultCard.AddThemeStyleboxOverride("panel", style);
    }

    // ---------------------------------------------------------------------
    // Quick start & reference
    // ---------------------------------------------------------------------

    private void BuildQuickStart(VBoxContainer root)
    {
        var section = Ui.Section(root, "Quick start",
            "No Seq server yet? It is free for local development. With Docker installed, this starts one on " +
            "[code]http://localhost:5341[/code], which is the default URL above. Replace the two placeholders first.");

        var card = Ui.Card(out var body);
        var header = Ui.HBox(8);
        header.AddChild(Ui.Text("Terminal", subtle: true));
        header.AddChild(EditorUi.Spacer());
        var copy = Ui.IconButton("ActionCopy", "Copy command", "Copy");
        copy.Flat = true;
        copy.Pressed += () => DisplayServer.ClipboardSet(DockerCommand);
        header.AddChild(copy);
        body.AddChild(header);

        var code = new PanelContainer();
        code.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0.22f), null, 4, 10));
        var label = Ui.Mono(DockerCommand);
        label.AddThemeColorOverride("font_color", Ui.Font.Lerp(Ui.Accent, 0.25f));
        code.AddChild(label);
        body.AddChild(code);

        body.AddChild(Ui.Rich(
            "Then switch the Seq output on, run the game and open Seq in the browser. " +
            "Not using Docker? Seq also has a native Windows installer: [url=https://datalust.co/download]datalust.co/download[/url].",
            subtle: true));

        section.AddChild(card);
    }

    private void BuildReference(VBoxContainer root)
    {
        var section = Ui.Section(root, "What gets sent",
            "Every event carries these properties in addition to the context dictionary you pass. " +
            "Context keys with the same name as a built-in property are ignored.");

        var card = Ui.Card(out var body);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", Ui.S(24));
        grid.AddThemeConstantOverride("v_separation", Ui.S(6));

        void AddRow(string name, string description)
        {
            var key = Ui.Mono(name);
            key.AddThemeColorOverride("font_color", Ui.Accent);
            grid.AddChild(key);
            grid.AddChild(Ui.Text(description, subtle: true, wrap: true));
        }

        AddRow("SessionId", "8-character id of the game session; filter one run with SessionId = '…'");
        AddRow("GameVersion", "application/config/version from Project Settings");
        AddRow("Channel", "The entry's channel");
        AddRow("Caller", "File:line (member) the entry was logged from");
        AddRow("@Level", "Debug, Information, Warning or Error");
        AddRow("@Exception", "Full exception text, when one was passed to Error()");
        body.AddChild(grid);

        body.AddChild(new HSeparator());
        body.AddChild(Ui.Text("Handy queries", subtle: true));

        foreach (var query in new[]
                 {
                     "Channel = 'Combat' and Damage > 50",
                     "@Level = 'Error'",
                     "SessionId = '3fa2c1d0'",
                     "GameVersion = '1.2.0' and @Level in ['Warning', 'Error']",
                 })
        {
            var row = Ui.HBox(8);
            var text = Ui.Mono(query);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
            var copy = Ui.IconButton("ActionCopy", "Copy query");
            string q = query;
            copy.Pressed += () => DisplayServer.ClipboardSet(q);
            row.AddChild(copy);
            body.AddChild(row);
        }

        section.AddChild(card);
    }

    private static string Escape(string text) => (text ?? string.Empty).Replace("[", "[lb]");
}
#endif
