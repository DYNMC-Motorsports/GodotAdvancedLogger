using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Interface for expendable Debug-Panel within the DebugScreen.
/// User can implement their own Debug-Panels to display custom Debug Information
/// 
/// Example: A "HoverUnitInfo" could display detailed information about the unit that the player is currently hovering
/// over with the mouse, such as health, stats, buffs/debuffs, etc. This panel would update its
/// content every frame based on the unit
/// </summary>
public interface IDebugPanel
{
    /// <summary>
    /// Unique Name of the Panel
    /// </summary>
    string PanelName { get; }

    /// <summary>
    /// Will be called when the visible state of the panel changes to visible.
    /// </summary>
    void OnPanelShow();

    /// <summary>
    /// Will be called when the visible state of the panel changes to hidden.
    /// Primarily used for cleanup of things not needed when the panel is hidden, such as unregistering listeners,
    /// cleaning up resources, etc.
    /// </summary>
    void OnPanelHide();

    /// <summary>
    /// Will be called every frame when the panel is visible.
    /// </summary>
    /// <param name="delta">Time since last update of the panel. In sync with DeltaTime of the main game loop.</param>
    void OnPanelUpdate(float delta);

    /// <summary>
    /// Return the Godot Control Node that defines the UI layout of the panel.
    /// You can create your custom UI here using Godot Controls. Will be used by the DebugPanel to show the content of the panel.
    /// </summary>
    /// <returns>A <see cref="Control"/> Node to be shown within the Debug Panel</returns>
    Control GetPanelUi();
}