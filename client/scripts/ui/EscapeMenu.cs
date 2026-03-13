using Godot;
using SandboxRPG;

/// <summary>
/// In-game pause / escape menu.
/// Opened by UIManager when ESC is pressed with an empty panel stack.
/// ESC while open = Resume (CloseOnEsc = true).
/// </summary>
public partial class EscapeMenu : BasePanel
{
    protected override void BuildUI()
    {
        // Dim backdrop
        AddChild(UIFactory.MakeOverlay());

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var col = UIFactory.MakeVBox(14);
        col.CustomMinimumSize = new Vector2(300, 0);
        col.Alignment = BoxContainer.AlignmentMode.Center;
        center.AddChild(col);

        col.AddChild(UIFactory.MakeTitle("Paused", 28));
        col.AddChild(UIFactory.MakeSeparator());

        var resumeBtn = UIFactory.MakeButton("Resume", 18, new Vector2(300, 52));
        resumeBtn.Pressed += () => UIManager.Instance.Pop();
        col.AddChild(resumeBtn);

        var settingsBtn = UIFactory.MakeButton("Settings", 16, new Vector2(300, 46));
        settingsBtn.Pressed += () => UIManager.Instance.Push(new SettingsUI());
        col.AddChild(settingsBtn);

        col.AddChild(UIFactory.MakeSeparator());

        var leaveBtn = UIFactory.MakeButton("Leave to Menu", 16, new Vector2(300, 46));
        leaveBtn.Pressed += OnLeavePressed;
        leaveBtn.AddThemeColorOverride("font_color", UIFactory.ColourMuted);
        col.AddChild(leaveBtn);

        var quitBtn = UIFactory.MakeButton("Quit Game", 16, new Vector2(300, 46));
        quitBtn.Pressed += () => col.GetTree().Quit();
        quitBtn.AddThemeColorOverride("font_color", UIFactory.ColourDanger);
        col.AddChild(quitBtn);
    }

    private void OnLeavePressed()
    {
        UIManager.Instance.PopAll();
        // Disconnect from server gracefully (connection will clean up)
        SceneRouter.GoTo(GameScene.MainMenu);
    }
}
