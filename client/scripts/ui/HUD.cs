using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Heads-up display — health, stamina, crosshair, connection status, player info, and Hotbar.
/// ESC is now owned by UIManager; HUD only shows always-on UI elements.
/// </summary>
public partial class HUD : Control
{
    private Label       _statusLabel      = null!;
    private ProgressBar _healthBar        = null!;
    private ProgressBar _staminaBar       = null!;
    private Label       _playerInfoLabel  = null!;

    public override void _Ready()
    {
        BuildUI();
        ConnectSignals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        // I or C → toggle the combined Inventory + Crafting panel
        if (key.PhysicalKeycode is Key.I or Key.C)
        {
            UIManager.Instance.Toggle<InventoryCraftingPanel>();
            GetViewport().SetInputAsHandled();
        }
    }

    // =========================================================================
    // UI CONSTRUCTION
    // =========================================================================

    private void BuildUI()
    {
        // Crosshair (centre)
        var crosshair = new Label
        {
            Text = "+",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            LayoutMode          = 1,
            AnchorsPreset       = (int)LayoutPreset.Center,
        };
        crosshair.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        crosshair.AddThemeFontSizeOverride("font_size", 24);
        AddChild(crosshair);

        // Connection status (top right)
        _statusLabel = new Label
        {
            Text = "Connecting...",
            HorizontalAlignment = HorizontalAlignment.Right,
            LayoutMode          = 1,
            AnchorsPreset       = (int)LayoutPreset.TopRight,
            OffsetLeft          = -250,
            OffsetTop           = 10,
            OffsetRight         = -10,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(1, 0.8f, 0.2f));
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_statusLabel);

        // Health / stamina bars (bottom left, above hotbar) — wrapped in a dark panel
        var barsPanel = new PanelContainer
        {
            LayoutMode    = 1,
            AnchorsPreset = (int)LayoutPreset.BottomLeft,
            OffsetLeft    = 10,
            OffsetBottom  = -90,
            OffsetTop     = -175,
            OffsetRight   = 230,
        };
        barsPanel.AddThemeStyleboxOverride("panel", UIFactory.MakeDarkPanelStyle(0.45f));
        AddChild(barsPanel);

        var barsContainer = new VBoxContainer();
        barsContainer.AddThemeConstantOverride("separation", 2);
        barsPanel.AddChild(barsContainer);

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
            Text          = "",
            LayoutMode    = 1,
            AnchorsPreset = (int)LayoutPreset.TopLeft,
            OffsetLeft    = 10,
            OffsetTop     = 30,
        };
        _playerInfoLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.8f));
        _playerInfoLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_playerInfoLabel);

        // Hotbar (always visible, bottom centre)
        var hotbar = new Hotbar();
        AddChild(hotbar);
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
            _healthBar.Value     = player.Health;
            _healthBar.MaxValue  = player.MaxHealth;
            _staminaBar.Value    = player.Stamina;
            _staminaBar.MaxValue = player.MaxStamina;
            _playerInfoLabel.Text = $"{player.Name}\nPos: ({player.PosX:F1}, {player.PosY:F1}, {player.PosZ:F1})";
        }

        int onlineCount = 0;
        foreach (var p in GameManager.Instance.GetAllPlayers())
            if (p.IsOnline) onlineCount++;

        if (GameManager.Instance.IsConnected)
            _statusLabel.Text = $"Online ({onlineCount} players)";
    }
}
