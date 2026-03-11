using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Heads-up display showing health, stamina, crosshair, and connection status.
/// </summary>
public partial class HUD : Control
{
    private Label _statusLabel = null!;
    private ProgressBar _healthBar = null!;
    private ProgressBar _staminaBar = null!;
    private Label _crosshair = null!;
    private Label _playerInfoLabel = null!;

    public override void _Ready()
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
            OffsetLeft = -250,
            OffsetTop = 10,
            OffsetRight = -10,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(1, 0.8f, 0.2f));
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_statusLabel);

        // Health bar (bottom left)
        var barsContainer = new VBoxContainer
        {
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.BottomLeft,
            OffsetLeft = 20,
            OffsetBottom = -20,
            OffsetTop = -80,
            OffsetRight = 220,
        };
        AddChild(barsContainer);

        var healthLabel = new Label { Text = "HP" };
        healthLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
        healthLabel.AddThemeFontSizeOverride("font_size", 12);
        barsContainer.AddChild(healthLabel);

        _healthBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
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
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
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
            OffsetTop = 30,
        };
        _playerInfoLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.8f));
        _playerInfoLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_playerInfoLabel);

        // Connect signals
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

    public override void _Process(double delta)
    {
        var player = GameManager.Instance.GetLocalPlayer();
        if (player != null)
        {
            var p = player;
            _healthBar.Value = p.Health;
            _healthBar.MaxValue = p.MaxHealth;
            _staminaBar.Value = p.Stamina;
            _staminaBar.MaxValue = p.MaxStamina;
            _playerInfoLabel.Text = $"{p.Name}\nPos: ({p.PosX:F1}, {p.PosY:F1}, {p.PosZ:F1})";
        }

        // Count online players
        int onlineCount = 0;
        foreach (var p in GameManager.Instance.GetAllPlayers())
        {
            if (p.IsOnline) onlineCount++;
        }
        if (GameManager.Instance.IsConnected)
        {
            _statusLabel.Text = $"Online ({onlineCount} players)";
        }
    }
}
