using Godot;
using SandboxRPG;

/// <summary>
/// Character creation screen — shown on first login or when the player's name
/// is still the auto-generated "Player_XXXXXXXX" default.
/// Lets the player choose a display name and a colour.
/// </summary>
public partial class CharacterSetup : Control
{
    private LineEdit          _nameEdit      = null!;
    private ColorPickerButton _colorBtn      = null!;
    private Label             _errorLabel    = null!;
    private Button            _confirmBtn    = null!;

    // =========================================================================
    // GODOT LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        BuildUI();
        PreFill();
    }

    // =========================================================================
    // UI CONSTRUCTION
    // =========================================================================

    private void BuildUI()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(UIFactory.MakeDarkBackground(new Color(0.04f, 0.06f, 0.10f, 1f)));

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UIFactory.MakePanel(new Vector2(400, 0));
        center.AddChild(panel);

        var col = UIFactory.MakeVBox(18);
        col.CustomMinimumSize = new Vector2(360, 0);
        panel.AddChild(col);

        col.AddChild(UIFactory.MakeTitle("Create Character", 26));
        col.AddChild(UIFactory.MakeSeparator());

        // Name
        col.AddChild(UIFactory.MakeLabel("Display Name", 13, UIFactory.ColourMuted));
        _nameEdit = UIFactory.MakeLineEdit("Enter your name…", 16, 360);
        _nameEdit.MaxLength = 32;
        col.AddChild(_nameEdit);

        // Colour
        col.AddChild(UIFactory.MakeLabel("Player Colour", 13, UIFactory.ColourMuted));
        _colorBtn = new ColorPickerButton();
        _colorBtn.CustomMinimumSize = new Vector2(360, 44);
        _colorBtn.EditAlpha = false;
        col.AddChild(_colorBtn);

        col.AddChild(UIFactory.MakeSeparator());

        // Error label
        _errorLabel = UIFactory.MakeLabel("", 13, UIFactory.ColourDanger);
        _errorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_errorLabel);

        // Confirm
        _confirmBtn = UIFactory.MakeButton("Enter World", 18, new Vector2(360, 52));
        _confirmBtn.Pressed += OnConfirmPressed;
        col.AddChild(_confirmBtn);
    }

    private void PreFill()
    {
        // Use locally cached name/colour if available
        string cachedName  = PlayerPrefs.LoadName();
        string cachedColor = PlayerPrefs.LoadColorHex();

        _nameEdit.Text = string.IsNullOrEmpty(cachedName) ? "" : cachedName;

        if (Color.HtmlIsValid(cachedColor))
            _colorBtn.Color = new Color(cachedColor);
        else
            _colorBtn.Color = new Color(UIFactory.ColourAccent);
    }

    // =========================================================================
    // CONFIRM
    // =========================================================================

    private void OnConfirmPressed()
    {
        var name = _nameEdit.Text.Trim();
        if (name.Length < 2)
        {
            _errorLabel.Text = "Name must be at least 2 characters.";
            return;
        }
        if (name.Length > 32)
        {
            _errorLabel.Text = "Name must be 32 characters or fewer.";
            return;
        }

        _errorLabel.Text = "";
        _confirmBtn.Disabled = true;

        string colorHex = "#" + _colorBtn.Color.ToHtml(false);

        // Send to server
        GameManager.Instance.SetPlayerName(name);
        GameManager.Instance.SetPlayerColor(colorHex);

        // Cache locally
        PlayerPrefs.SaveAll(name, colorHex);

        // Head to game
        SceneRouter.GoTo(GameScene.Game);
    }
}
