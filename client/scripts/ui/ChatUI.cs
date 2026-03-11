using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Chat interface: shows messages and allows sending.
/// Press T to open chat input.
/// </summary>
public partial class ChatUI : Control
{
	private RichTextLabel _chatLog = null!;
	private LineEdit _chatInput = null!;
	private PanelContainer _chatPanel = null!;
	private bool _chatOpen;
	private readonly List<string> _messages = new();
	private const int MaxMessages = 50;

	public override void _Ready()
	{
		// Chat panel (bottom left area)
		_chatPanel = new PanelContainer
		{
			LayoutMode = 1,
			AnchorsPreset = (int)LayoutPreset.BottomLeft,
			OffsetLeft = 10,
			OffsetBottom = -110,
			OffsetTop = -350,
			OffsetRight = 450,
		};

		// Semi-transparent background
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0, 0, 0, 0.4f),
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
		};
		_chatPanel.AddThemeStyleboxOverride("panel", style);
		AddChild(_chatPanel);

		var vbox = new VBoxContainer();
		_chatPanel.AddChild(vbox);

		_chatLog = new RichTextLabel
		{
			BbcodeEnabled = true,
			ScrollFollowing = true,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			FitContent = false,
		};
		_chatLog.AddThemeColorOverride("default_color", new Color(1, 1, 1, 0.9f));
		_chatLog.AddThemeFontSizeOverride("normal_font_size", 14);
		vbox.AddChild(_chatLog);

		_chatInput = new LineEdit
		{
			PlaceholderText = "Type message...",
			Visible = false,
			CustomMinimumSize = new Vector2(0, 30),
		};
		_chatInput.TextSubmitted += OnChatSubmitted;
		vbox.AddChild(_chatInput);

		// Connect to GameManager
		GameManager.Instance.ChatMessageReceived += OnChatMessageReceived;

		// Add welcome message
		AddMessage("[color=yellow]Welcome to SandboxRPG! Press T to chat.[/color]");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("chat") && !_chatOpen)
		{
			OpenChat();
			GetViewport().SetInputAsHandled();
		}
		else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape && _chatOpen)
		{
			CloseChat();
			GetViewport().SetInputAsHandled();
		}
	}

	private void OpenChat()
	{
		_chatOpen = true;
		_chatInput.Visible = true;
		_chatInput.GrabFocus();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void CloseChat()
	{
		_chatOpen = false;
		_chatInput.Visible = false;
		_chatInput.Clear();
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void OnChatSubmitted(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			GameManager.Instance.SendChatMessage(text);
		}
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
		{
			_messages.RemoveAt(0);
		}

		_chatLog.Clear();
		foreach (var msg in _messages)
		{
			_chatLog.AppendText(msg + "\n");
		}
	}
}
