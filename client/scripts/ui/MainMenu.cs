using Godot;
using SandboxRPG;

/// <summary>
/// Main menu — entry point of the game.
/// Reacts to GameManager.StateChanged to drive scene transitions.
/// All UI built in code via UIFactory.
/// </summary>
public partial class MainMenu : Control
{
    private Button _continueBtn   = null!;
    private Button _newCharBtn    = null!;
    private Label  _statusLabel   = null!;
    private bool   _connecting    = false;

    // =========================================================================
    // GODOT LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        BuildUI();
        RefreshButtons();

        // React to GameManager state changes
        GameManager.Instance.StateChanged += OnStateChanged;
        GameManager.Instance.ConnectionFailed += OnConnectionFailed;
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance is not null)
        {
            GameManager.Instance.StateChanged -= OnStateChanged;
            GameManager.Instance.ConnectionFailed -= OnConnectionFailed;
        }
    }

    // =========================================================================
    // UI CONSTRUCTION
    // =========================================================================

    private void BuildUI()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Background
        AddChild(UIFactory.MakeDarkBackground(new Color(0.04f, 0.06f, 0.10f, 1f)));

        // Centre column
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var col = UIFactory.MakeVBox(20);
        col.CustomMinimumSize = new Vector2(340, 0);
        col.Alignment = BoxContainer.AlignmentMode.Center;
        center.AddChild(col);

        // Title
        col.AddChild(UIFactory.MakeTitle("SandboxRPG", 36));
        col.AddChild(UIFactory.MakeLabel("A multiplayer sandbox survival game", 13, UIFactory.ColourMuted));
        col.AddChild(UIFactory.MakeSeparator());

        // Continue button (only shown when a token exists)
        _continueBtn = UIFactory.MakeButton("Continue", 18, new Vector2(340, 52));
        _continueBtn.Pressed += OnContinuePressed;
        col.AddChild(_continueBtn);

        // New Character button
        _newCharBtn = UIFactory.MakeButton("New Character", 18, new Vector2(340, 52));
        _newCharBtn.Pressed += OnNewCharacterPressed;
        col.AddChild(_newCharBtn);

        // Settings button
        var settingsBtn = UIFactory.MakeButton("Settings", 16, new Vector2(340, 44));
        settingsBtn.Pressed += OnSettingsPressed;
        col.AddChild(settingsBtn);

        // Quit button
        var quitBtn = UIFactory.MakeButton("Quit", 16, new Vector2(340, 44));
        quitBtn.Pressed += () => GetTree().Quit();
        col.AddChild(quitBtn);

        col.AddChild(UIFactory.MakeSeparator());

        // Status label
        _statusLabel = UIFactory.MakeLabel("", 13, UIFactory.ColourMuted);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_statusLabel);
    }

    private void RefreshButtons()
    {
        bool hasToken = GameManager.Instance.HasSavedToken();
        _continueBtn.Visible = hasToken;
    }

    // =========================================================================
    // BUTTON HANDLERS
    // =========================================================================

    private void OnContinuePressed()
    {
        if (_connecting) return;
        _connecting = true;
        SetStatus("Connecting…");
        SetButtonsEnabled(false);
        GameManager.Instance.Connect();
    }

    private void OnNewCharacterPressed()
    {
        if (_connecting) return;
        _connecting = true;
        SetStatus("Connecting as new character…");
        SetButtonsEnabled(false);
        GameManager.Instance.ConnectFresh();
    }

    private void OnSettingsPressed()
    {
        var settings = new SettingsUI();
        settings.ReturnToMenu = true;
        AddChild(settings);
    }

    // =========================================================================
    // STATE REACTIONS
    // =========================================================================

    private void OnStateChanged(int rawState)
    {
        var state = (GameState)rawState;
        switch (state)
        {
            case GameState.Connecting:
                SetStatus("Connecting…");
                break;

            case GameState.Connected:
                SetStatus("Syncing data…");
                break;

            case GameState.CharacterSetup:
                SceneRouter.GoTo(GameScene.CharacterSetup);
                break;

            case GameState.InGame:
                SceneRouter.GoTo(GameScene.Game);
                break;

            case GameState.Disconnected:
                _connecting = false;
                SetButtonsEnabled(true);
                RefreshButtons();
                SetStatus("");
                break;
        }
    }

    private void OnConnectionFailed(string reason)
    {
        _connecting = false;
        SetButtonsEnabled(true);
        SetStatus($"Connection failed: {reason}", UIFactory.ColourDanger);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void SetStatus(string text, Color? colour = null)
    {
        _statusLabel.Text = text;
        _statusLabel.AddThemeColorOverride("font_color", colour ?? UIFactory.ColourMuted);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _continueBtn.Disabled  = !enabled;
        _newCharBtn.Disabled   = !enabled;
    }
}
