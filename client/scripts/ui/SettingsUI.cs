using Godot;
using SandboxRPG;

/// <summary>
/// Settings panel — change player name and colour.
/// Pushed onto UIManager stack by EscapeMenu or MainMenu.
/// Mouse mode and ESC are handled by UIManager.
/// </summary>
public partial class SettingsUI : BasePanel
{
    private LineEdit          _nameEdit    = null!;
    private ColorPickerButton _colorBtn    = null!;
    private Label             _statusLabel = null!;

    // =========================================================================
    // BASE PANEL
    // =========================================================================

    public override void OnPushed()
    {
        LoadCurrentValues();
    }

    protected override void BuildUI()
    {
        // Dim overlay
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

        col.AddChild(UIFactory.MakeLabel("Display Name", 13, UIFactory.ColourMuted));
        _nameEdit = UIFactory.MakeLineEdit("Your name…", 15, 380);
        _nameEdit.MaxLength = 32;
        col.AddChild(_nameEdit);

        col.AddChild(UIFactory.MakeLabel("Player Colour", 13, UIFactory.ColourMuted));
        _colorBtn = new ColorPickerButton();
        _colorBtn.CustomMinimumSize = new Vector2(380, 44);
        _colorBtn.EditAlpha = false;
        col.AddChild(_colorBtn);

        col.AddChild(UIFactory.MakeSeparator());

        _statusLabel = UIFactory.MakeLabel("", 12, UIFactory.ColourMuted);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_statusLabel);

        var row = UIFactory.MakeHBox(12);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(row);

        var saveBtn = UIFactory.MakeButton("Save", 16, new Vector2(180, 46));
        saveBtn.Pressed += OnSavePressed;
        row.AddChild(saveBtn);

        var backBtn = UIFactory.MakeButton("Back", 16, new Vector2(180, 46));
        backBtn.Pressed += () => UIManager.Instance.Pop();
        row.AddChild(backBtn);
    }

    // =========================================================================
    // DATA
    // =========================================================================

    private void LoadCurrentValues()
    {
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

    private void OnSavePressed()
    {
        var name = _nameEdit.Text.Trim();
        if (name.Length < 2)
        {
            _statusLabel.Text = "Name must be at least 2 characters.";
            _statusLabel.AddThemeColorOverride("font_color", UIFactory.ColourDanger);
            return;
        }
        if (name.Length > 32)
        {
            _statusLabel.Text = "Name must be at most 32 characters.";
            _statusLabel.AddThemeColorOverride("font_color", UIFactory.ColourDanger);
            return;
        }

        string colorHex = "#" + _colorBtn.Color.ToHtml(false);
        PlayerPrefs.SaveAll(name, colorHex);

        if (GameManager.Instance.IsConnected)
        {
            GameManager.Instance.SetPlayerName(name);
            GameManager.Instance.SetPlayerColor(colorHex);
        }

        _statusLabel.Text = "Saved!";
        _statusLabel.AddThemeColorOverride("font_color", UIFactory.ColourAccent);
    }
}
