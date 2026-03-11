using Godot;
using SandboxRPG;

/// <summary>
/// Settings overlay — opened from MainMenu or from in-game HUD.
/// Builds its own UI in code using UIFactory.
/// Set <see cref="ReturnToMenu"/> = true when opened from the main menu so
/// the Back button doesn't need to do any extra work.
/// </summary>
public partial class SettingsUI : Control
{
    /// <summary>When true the overlay just frees itself on Back. When false it
    /// also releases the mouse cursor (in-game usage).</summary>
    public bool ReturnToMenu { get; set; } = false;

    private LineEdit          _nameEdit   = null!;
    private ColorPickerButton _colorBtn   = null!;
    private Label             _statusLabel = null!;

    // =========================================================================
    // GODOT LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        BuildUI();
        LoadCurrentValues();
    }

    // =========================================================================
    // UI CONSTRUCTION
    // =========================================================================

    private void BuildUI()
    {
        // Dim overlay behind the panel
        AddChild(UIFactory.MakeOverlay());

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UIFactory.MakePanel(new Vector2(420, 0));
        center.AddChild(panel);

        var col = UIFactory.MakeVBox(16);
        col.CustomMinimumSize = new Vector2(380, 0);
        panel.AddChild(col);

        col.AddChild(UIFactory.MakeTitle("Settings", 24));
        col.AddChild(UIFactory.MakeSeparator());

        // Display Name
        col.AddChild(UIFactory.MakeLabel("Display Name", 13, UIFactory.ColourMuted));
        _nameEdit = UIFactory.MakeLineEdit("Your name…", 15, 380);
        _nameEdit.MaxLength = 32;
        col.AddChild(_nameEdit);

        // Player Colour
        col.AddChild(UIFactory.MakeLabel("Player Colour", 13, UIFactory.ColourMuted));
        _colorBtn = new ColorPickerButton();
        _colorBtn.CustomMinimumSize = new Vector2(380, 44);
        _colorBtn.EditAlpha = false;
        col.AddChild(_colorBtn);

        col.AddChild(UIFactory.MakeSeparator());

        // Status
        _statusLabel = UIFactory.MakeLabel("", 12, UIFactory.ColourMuted);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_statusLabel);

        // Buttons row
        var row = UIFactory.MakeHBox(12);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(row);

        var saveBtn = UIFactory.MakeButton("Save", 16, new Vector2(180, 46));
        saveBtn.Pressed += OnSavePressed;
        row.AddChild(saveBtn);

        var backBtn = UIFactory.MakeButton("Back", 16, new Vector2(180, 46));
        backBtn.Pressed += OnBackPressed;
        row.AddChild(backBtn);
    }

    private void LoadCurrentValues()
    {
        // Prefer live server data, fall back to local cache
        var player = GameManager.Instance.GetLocalPlayer();
        if (player is not null)
        {
            _nameEdit.Text = player.Name.StartsWith("Player_") ? PlayerPrefs.LoadName() : player.Name;
            string colorHex = player.ColorHex ?? PlayerPrefs.LoadColorHex();
            if (Color.HtmlIsValid(colorHex))
                _colorBtn.Color = new Color(colorHex);
        }
        else
        {
            _nameEdit.Text = PlayerPrefs.LoadName();
            string colorHex = PlayerPrefs.LoadColorHex();
            if (Color.HtmlIsValid(colorHex))
                _colorBtn.Color = new Color(colorHex);
        }
    }

    // =========================================================================
    // HANDLERS
    // =========================================================================

    private void OnSavePressed()
    {
        var name = _nameEdit.Text.Trim();
        if (name.Length < 2)
        {
            _statusLabel.Text = "Name must be at least 2 characters.";
            _statusLabel.AddThemeColorOverride("font_color", UIFactory.ColourDanger);
            return;
        }

        string colorHex = "#" + _colorBtn.Color.ToHtml(false);

        // Persist locally
        PlayerPrefs.SaveAll(name, colorHex);

        // Send to server if connected
        if (GameManager.Instance.IsConnected)
        {
            GameManager.Instance.SetPlayerName(name);
            GameManager.Instance.SetPlayerColor(colorHex);
        }

        _statusLabel.Text = "Saved!";
        _statusLabel.AddThemeColorOverride("font_color", UIFactory.ColourAccent);
    }

    private void OnBackPressed()
    {
        if (!ReturnToMenu)
        {
            // In-game: release mouse so the player can look around again
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        QueueFree();
    }
}
