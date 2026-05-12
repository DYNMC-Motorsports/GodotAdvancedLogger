using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// TEMPLATE for a custom Debug Panel. Copy this class, rename it and implement your own panel by filling in the methods and UI.
/// 
/// NOTE: This example is NOT using the ContextLogger to not fill up the Log with unnecessary message. Please switch
/// to the ContextLogger for your logging purposes!
/// 
/// USAGE GUIDE:
/// 1. Copy this file
/// 2. Rename it
/// 3. Change the class name and namespace
/// 4. Implement missing methods
/// 5. Register your panel with: DebugScreen.Instance.RegisterDebugPanel(new MyDebugPanel());
/// </summary>
public class CustomDebugPanelTemplate : IDebugPanel
{
    #region Properties

    /// <summary>
    /// Name of the panel (will be shown within the top bar)
    /// IMPORTANT: Name must be unique among all registered panels!
    /// </summary>
    public string PanelName => "Your Panel Name";

    // UI-Components
    private Label _statusLabel;
    private VBoxContainer _mainContainer;
    private string _pendingStatusText = "Status: Active";

    #endregion

    #region Lifecycle Methods

    /// <summary>
    /// Will be called when the visible state of the panel changes to visible.
    /// Used for initialization
    /// </summary>
    public void OnPanelShow()
    {
        GD.Print($"[{PanelName}] panel will be shown");
        // TODO: Initialize necessary things
        // e.g. register listeners, load resources, ...
    }

    /// <summary>
    /// Will be called when the visible state of the panel changes to hidden.
    /// Use this to cleanup any things not needed when the panel state is hidden.
    /// </summary>
    public void OnPanelHide()
    {
        GD.Print($"[{PanelName}] panel will be hidden");
        // TODO: Cleanup
        // e.g. remove listeners, cleanup resources
    }

    /// <summary>
    /// Will be called on every frame when the panel is visible
    /// </summary>
    public void OnPanelUpdate(float delta)
    {
        // IMPORTANT: Keep this method performant!
    }

    /// <summary>
    /// This method is used to return the content displayed in the panel. You can create your custom UI here using Godot Controls.
    /// </summary>
    /// <returns>Root Control Node of the panel</returns>
    public Control GetPanelUi()
    {
        // Create a container for our UI elements
        _mainContainer = new VBoxContainer();

        #region EXAMPLE UI - REPLACE 

        // Title
        var titleLabel = new Label { Text = PanelName };
        _mainContainer.AddChild(titleLabel);

        // Description
        var descLabel = new Label 
        { 
            Text = "Implement your own UI here",
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _mainContainer.AddChild(descLabel);

        // Separator
        _mainContainer.AddChild(new HSeparator());

        // Status-Label
        _statusLabel = new Label { Text = _pendingStatusText };
        _mainContainer.AddChild(_statusLabel);

        // Example Button
        var testButton = new Button { Text = "Klick mich!" };
        testButton.Pressed += OnTestButtonPressed;
        _mainContainer.AddChild(testButton);

        // Margin-Filler
        var spacer = new Control 
        { 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        _mainContainer.AddChild(spacer);

        #endregion

        ApplyPendingStatusText();

        return _mainContainer;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Example - Will be called once the TestButton is being pressed
    /// </summary>
    private void OnTestButtonPressed()
    {
        GD.Print($"[{PanelName}] button has been pressed!");
        UpdateUi($"Status: Button has been pressed at: {System.DateTime.Now:HH:mm:ss}");
    }

    /// <summary>
    /// Updates a label in the UI.
    /// If the UI has not been created yet, save the text in memory.
    /// </summary>
    public void UpdateUi(string text)
    {
        _pendingStatusText = text;
        ApplyPendingStatusText();
    }

    private void ApplyPendingStatusText()
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = _pendingStatusText;
        }
    }

    #endregion

    #region Further Tips

    /*

    HELPFUL TIPS:

    1. NAMING CONVENTION:
        - Class: [YourName]DebugPanel
        - Filename: [YourName]DebugPanel.cs
        - PanelName: "Short unique name"

    2. UI Components
        - Use Standard Godot Controls: Label, Button, CheckBox, etc.
        - HBoxContainer for horizontal layouts
        - VBoxContainer for vertical layouts
        - ScrollContainer for scrollable content

    3. PERFORMANCE:
        - OnPanelUpdate will be called once every frame!
        - If possible, cache expensive operations
        - Use delta time for time based debugging
        - Limit the number of updates to a specific framerate

    4. THREAD-SECURITY:
        - Godot UI must be updated on the main thread!
        - If the UI does not exist yet, save text or data in the cache to be displayed once the UI is loaded!

    5. REGISTRATION:
       To register the panel:

       var panel = new CustomDebugPanelTemplate();
       DebugScreen.Instance.RegisterDebugPanel(panel);

    6. Access to other panels:
       var logsPanel = DebugScreen.Instance.GetLogsPanel();

    7. Use the build in Logger to log things in the build in logger-output
    
    8. Don't hesitate to open up an issue over on GitHub if you have any problems / questions!

    */

    #endregion
}

