// client/mods/npcs/ui/NpcDialoguePanel.cs
using Godot;

namespace SandboxRPG;

public partial class NpcDialoguePanel : BasePanel
{
    private readonly string _npcType;
    private int _currentLine;
    private Label _dialogueLabel = null!;
    private string[] _lines = System.Array.Empty<string>();

    public NpcDialoguePanel(string npcType)
    {
        _npcType = npcType;
    }

    protected override void BuildUI()
    {
        _lines = DialogueRegistry.Get(_npcType) ?? new[] { "..." };
        var visual = NpcVisualRegistry.Get(_npcType);
        string name = visual?.DisplayName ?? _npcType;

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UIFactory.MakePanel(new Vector2(500, 250));
        center.AddChild(panel);

        var vbox = UIFactory.MakeVBox(10);
        panel.AddChild(vbox);

        vbox.AddChild(UIFactory.MakeTitle(name, 20));
        vbox.AddChild(UIFactory.MakeSeparator());

        _dialogueLabel = UIFactory.MakeLabel(_lines[0], 16);
        _dialogueLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dialogueLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(_dialogueLabel);

        var btnRow = UIFactory.MakeHBox(10);
        btnRow.Alignment = BoxContainer.AlignmentMode.End;
        vbox.AddChild(btnRow);

        var nextBtn = UIFactory.MakeButton("Next", 14, new Vector2(80, 32));
        nextBtn.Pressed += OnNext;
        btnRow.AddChild(nextBtn);

        var closeBtn = UIFactory.MakeButton("Close", 14, new Vector2(80, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        btnRow.AddChild(closeBtn);
    }

    private void OnNext()
    {
        _currentLine++;
        if (_currentLine >= _lines.Length)
            _currentLine = 0; // Loop back
        _dialogueLabel.Text = _lines[_currentLine];
    }
}
