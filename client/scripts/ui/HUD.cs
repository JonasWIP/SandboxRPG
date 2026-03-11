using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Heads-up display — health, stamina, crosshair, player info, and Settings overlay.
/// ESC opens the Settings panel; the PlayerController no longer handles ESC.
/// </summary>
public partial class HUD : Control
{
    private Label _statusLabel = null!;
    private ProgressBar _healthBar = null!;
    private ProgressBar _staminaBar = null!;
    private Label _crosshair = null!;
    private Label _playerInfoLabel = null!;
    private SettingsUI? _settingsOverlay;

    public override void _Ready()
    {
        BuildUI();
        ConnectSignals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (_settingsOverlay is not null) return; // already open
            OpenSettings();
            GetViewport().SetInputAsHandled();
        }
    }

    // =========================================================================
    // UI CONSTRUCTION
    // =========================================================================

    private void BuildUI()
    {
        // Crosshair
        _crosshair = new Label
        {
            Text = "+",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.Center,
        };
        _crosshair.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        _crosshair.AddThemeFontSizeOverride("font_size", 24);
        AddChild(_crosshair);

        // Connection status (top right)
        _statusLabel = new Label
        {
            Text = "Connecting...",
            HorizontalAlignment = HorizontalAlignment.Right,
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.TopRight,
            OffsetLeft   = -250,
            OffsetTop    = 10,
            OffsetRight  = -10,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(1, 0.8f, 0.2f));
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_statusLabel);

        // Settings button (top right, below status)
        var settingsBtn = new Button
        {
            Text = "⚙ Settings",
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.TopRight,
            OffsetLeft  = -130,
            OffsetTop   = 34,
            OffsetRight = -10,
            OffsetBottom = 62,
        };
        settingsBtn.AddThemeFontSizeOverride("font_size", 13);
        settingsBtn.Pressed += OpenSettings;
        AddChild(settingsBtn);

        // Health / stamina bars (bottom left)
        var barsContainer = new VBoxContainer
        {
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.BottomLeft,
            OffsetLeft   = 20,
            OffsetBottom = -20,
            OffsetTop    = -80,
            OffsetRight  = 220,
        };
        AddChild(barsContainer);

        var healthLabel = new Label { Text = "HP" };
        healthLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
        healthLabel.AddThemeFontSizeOverride("font_size", 12);
        barsContainer.AddChild(healthLabel);

        _healthBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 100,
            CustomMinimumSize = new Vector2(200, 20),
            ShowPercentage = false,
        };
        barsContainer.AddChild(_healthBar);

        var staminaLabel = new Label { Text = "STA" };
        staminaLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.8f, 1f));
        staminaLabel.AddThemeFontSizeOverride("font_size", 12);
        barsContainer.AddChild(staminaLabel);

        _staminaBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 100,
            CustomMinimumSize = new Vector2(200, 16),
            ShowPercentage = false,
        };
        barsContainer.AddChild(_staminaBar);

        // Player info (top left)
        _playerInfoLabel = new Label
        {
            Text = "",
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.TopLeft,
            OffsetLeft = 10,
            OffsetTop  = 30,
        };
        _playerInfoLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.8f));
        _playerInfoLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_playerInfoLabel);
    }

    private void ConnectSignals()
    {
        var gm = GameManager.Instance;
        gm.Connected += () => _statusLabel.Text = "Connected";
        gm.Disconnected += () =>
        {
            _statusLabel.Text = "Disconnected";
            _statusLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
        };
        gm.SubscriptionApplied += () =>
        {
            _statusLabel.Text = "Online";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        };
    }

    // =========================================================================
    // PROCESS
    // =========================================================================

    public override void _Process(double delta)
    {
        var player = GameManager.Instance.GetLocalPlayer();
        if (player != null)
        {
            _healthBar.Value  = player.Health;
            _healthBar.MaxValue  = player.MaxHealth;
            _staminaBar.Value = player.Stamina;
            _staminaBar.MaxValue = player.MaxStamina;
            _playerInfoLabel.Text = $"{player.Name}\nPos: ({player.PosX:F1}, {player.PosY:F1}, {player.PosZ:F1})";
        }

        int onlineCount = 0;
        foreach (var p in GameManager.Instance.GetAllPlayers())
            if (p.IsOnline) onlineCount++;

        if (GameManager.Instance.IsConnected)
            _statusLabel.Text = $"Online ({onlineCount} players)";
    }

    // =========================================================================
    // SETTINGS OVERLAY
    // =========================================================================

    private void OpenSettings()
    {
        if (_settingsOverlay is not null) return;

        // Show cursor so the player can interact with the UI
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _settingsOverlay = new SettingsUI { ReturnToMenu = false };
        _settingsOverlay.TreeExited += () => _settingsOverlay = null;
        AddChild(_settingsOverlay);
    }
}
