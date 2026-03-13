using Godot;
using System.Collections.Generic;

/// <summary>
/// Autoload singleton — central controller for all modal UI panels.
/// Owns the ESC key. Manages a panel stack and mouse mode.
///
/// Usage:
///   UIManager.Instance.Push(new MyPanel());   // open a panel
///   UIManager.Instance.Pop();                  // close topmost panel
///   UIManager.Instance.IsAnyOpen              // check if gameplay should be paused
/// </summary>
public partial class UIManager : Node
{
    public static UIManager Instance { get; private set; } = null!;

    // =========================================================================
    // STATE
    // =========================================================================

    private readonly List<BasePanel> _stack = new();
    private CanvasLayer _modalLayer = null!;
    private bool _chatFocused;

    public bool IsAnyOpen => _stack.Count > 0 || _chatFocused;
    public BasePanel? Top => _stack.Count > 0 ? _stack[^1] : null;

    // =========================================================================
    // SIGNALS
    // =========================================================================

    [Signal] public delegate void StackChangedEventHandler();

    // =========================================================================
    // GODOT LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        Instance = this;

        // Modal layer sits on top of all game UI (CanvasLayer layer 10)
        _modalLayer = new CanvasLayer { Layer = 10, Name = "ModalLayer" };
        AddChild(_modalLayer);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel")) return;

        if (_chatFocused)
        {
            SetChatFocused(false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_stack.Count == 0)
        {
            // Nothing open → open the in-game ESC / pause menu
            // Guard: only open EscapeMenu when actually in the game world
            if (GetTree().CurrentScene?.Name != "Main") return;
            Push(new EscapeMenu());
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Top is { CloseOnEsc: true })
        {
            Pop();
            GetViewport().SetInputAsHandled();
        }
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Push a panel onto the stack and display it.</summary>
    public void Push(BasePanel panel)
    {
        _modalLayer.AddChild(panel);
        _stack.Add(panel);
        panel.OnPushed();
        UpdateMouseMode();
        EmitSignal(SignalName.StackChanged);
    }

    /// <summary>Remove and free the topmost panel.</summary>
    public void Pop()
    {
        if (_stack.Count == 0) return;

        var panel = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        panel.OnPopped();
        panel.QueueFree();
        UpdateMouseMode();
        EmitSignal(SignalName.StackChanged);
    }

    /// <summary>Close all panels at once (e.g. when leaving to main menu).</summary>
    public void PopAll()
    {
        while (_stack.Count > 0)
        {
            var panel = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            panel.OnPopped();
            panel.QueueFree();
        }
        SetChatFocused(false);
        UpdateMouseMode();
        EmitSignal(SignalName.StackChanged);
    }

    /// <summary>
    /// Toggle the given panel type: if the top is already that type, pop it;
    /// otherwise push a new instance. Useful for I / C toggle behaviour.
    /// </summary>
    public void Toggle<T>() where T : BasePanel, new()
    {
        if (Top is T)
            Pop();
        else
            Push(new T());
    }

    /// <summary>
    /// Inform UIManager that the chat input field is focused/unfocused.
    /// Chat focus doesn't push a full panel but does affect mouse mode and ESC.
    /// </summary>
    public void SetChatFocused(bool focused)
    {
        _chatFocused = focused;
        UpdateMouseMode();
        EmitSignal(SignalName.StackChanged);
    }

    // =========================================================================
    // MOUSE MODE
    // =========================================================================

    private void UpdateMouseMode()
    {
        Input.MouseMode = _stack.Count > 0
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Captured;
    }
}
