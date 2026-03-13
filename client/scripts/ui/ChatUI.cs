using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Chat interface: always-visible message log, T to open input.
/// ESC to close input is handled by UIManager (SetChatFocused).
/// Mouse mode is managed by UIManager via SetChatFocused.
/// </summary>
public partial class ChatUI : Control
{
    private RichTextLabel  _chatLog    = null!;
    private LineEdit       _chatInput  = null!;
    private PanelContainer _chatPanel  = null!;
    private bool           _chatOpen;

    private readonly List<string> _messages = new();
    private const int MaxMessages = 50;

    public override void _Ready()
    {
        // Chat panel (bottom left area)
        _chatPanel = new PanelContainer
        {
            LayoutMode    = 1,
            AnchorsPreset = (int)LayoutPreset.BottomLeft,
            OffsetLeft    = 10,
            OffsetBottom  = -90,
            OffsetTop     = -330,
            OffsetRight   = 450,
        };

        var style = new StyleBoxFlat
        {
            BgColor                 = new Color(0, 0, 0, 0.4f),
            CornerRadiusBottomLeft  = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft     = 4, CornerRadiusTopRight    = 4,
            ContentMarginLeft       = 8, ContentMarginRight      = 8,
            ContentMarginTop        = 8, ContentMarginBottom     = 8,
        };
        _chatPanel.AddThemeStyleboxOverride("panel", style);
        AddChild(_chatPanel);

        var vbox = new VBoxContainer();
        _chatPanel.AddChild(vbox);

        _chatLog = new RichTextLabel
        {
            BbcodeEnabled     = true,
            ScrollFollowing   = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FitContent        = false,
        };
        _chatLog.AddThemeColorOverride("default_color", new Color(1, 1, 1, 0.9f));
        _chatLog.AddThemeFontSizeOverride("normal_font_size", 14);
        vbox.AddChild(_chatLog);

        _chatInput = new LineEdit
        {
            PlaceholderText   = "Type message…",
            Visible           = false,
            CustomMinimumSize = new Vector2(0, 30),
        };
        _chatInput.TextSubmitted += OnChatSubmitted;
        vbox.AddChild(_chatInput);

        GameManager.Instance.ChatMessageReceived += OnChatMessageReceived;

        // React when UIManager closes chat focus from outside (ESC)
        UIManager.Instance.StackChanged += OnStackChanged;

        AddMessage("[color=yellow]Welcome to SandboxRPG! Press T to chat.[/color]");
    }

    public override void _ExitTree()
    {
        if (UIManager.Instance is not null)
            UIManager.Instance.StackChanged -= OnStackChanged;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // T opens chat input
        if (@event.IsActionPressed("chat") && !_chatOpen)
        {
            OpenChat();
            GetViewport().SetInputAsHandled();
        }
    }

    // =========================================================================
    // CHAT OPEN / CLOSE
    // =========================================================================

    private void OpenChat()
    {
        _chatOpen = true;
        _chatInput.Visible = true;
        _chatInput.GrabFocus();
        UIManager.Instance.SetChatFocused(true);
    }

    private void CloseChat()
    {
        _chatOpen = false;
        _chatInput.Visible = false;
        _chatInput.Clear();
        UIManager.Instance.SetChatFocused(false);
    }

    /// <summary>
    /// Called when UIManager's stack changes — UIManager may have cleared chat focus
    /// (e.g. ESC pressed while chat was open), so sync the local state.
    /// </summary>
    private void OnStackChanged()
    {
        // If we were open but UIManager no longer considers chat focused, close input
        if (_chatOpen && !UIManager.Instance.IsAnyOpen)
            CloseChat();
    }

    // =========================================================================
    // EVENTS
    // =========================================================================

    private void OnChatSubmitted(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            GameManager.Instance.SendChatMessage(text);
        CloseChat();
    }

    private void OnChatMessageReceived(string senderName, string text)
    {
        AddMessage($"[color=cyan]{senderName}[/color]: {text}");
    }

    private void AddMessage(string bbcodeText)
    {
        _messages.Add(bbcodeText);
        if (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);

        _chatLog.Clear();
        foreach (var msg in _messages)
            _chatLog.AppendText(msg + "\n");
    }
}
