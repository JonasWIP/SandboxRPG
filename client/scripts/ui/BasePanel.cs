using Godot;

/// <summary>
/// Abstract base class for all modal UI panels managed by UIManager.
/// Handles full-rect anchoring and mouse click blocking automatically.
/// Subclasses implement BuildUI() to construct their content.
/// </summary>
public abstract partial class BasePanel : Control
{
    /// <summary>
    /// When true (default) UIManager will Pop this panel when ESC is pressed.
    /// Override to false for panels that handle ESC themselves (e.g. EscapeMenu does Resume).
    /// </summary>
    public virtual bool CloseOnEsc => true;

    /// <summary>Called by UIManager immediately after pushing this panel onto the stack.</summary>
    public virtual void OnPushed() { }

    /// <summary>Called by UIManager immediately before removing this panel from the stack.</summary>
    public virtual void OnPopped() { }

    /// <summary>Implement to construct all child nodes for this panel.</summary>
    protected abstract void BuildUI();

    public override void _Ready()
    {
        // Panels live inside UIManager's CanvasLayer (not a Control parent),
        // so anchor presets have no parent rect to compute against.
        // Explicitly size to the current viewport instead.
        var vpRect = GetViewport().GetVisibleRect();
        Position = vpRect.Position;
        Size = vpRect.Size;
        MouseFilter = MouseFilterEnum.Stop;
        BuildUI();
    }
}
