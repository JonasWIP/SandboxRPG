// client/mods/interactables/ui/SignPanel.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Modal panel for reading/editing a sign.
/// Non-owners see a read-only text display.
/// Owners see a text edit field with a Save button.
///
/// NOTE: SignText table and SetSignText reducer are not yet present in the
/// generated bindings. The panel compiles cleanly and will load/save text
/// once those are wired up server-side.
/// </summary>
public partial class SignPanel : BasePanel
{
    private readonly ulong _structureId;
    private readonly bool _isOwner;

    private Label _textDisplay = null!;
    private LineEdit? _textEdit;

    public SignPanel(ulong structureId, bool isOwner)
    {
        _structureId = structureId;
        _isOwner = isOwner;
    }

    protected override void BuildUI()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var outerPanel = UIFactory.MakePanel(new Vector2(480, 280));
        center.AddChild(outerPanel);

        var root = UIFactory.MakeVBox(12);
        outerPanel.AddChild(root);

        // Title row
        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(titleRow);

        titleRow.AddChild(UIFactory.MakeTitle("Sign", 20));

        var closeBtn = UIFactory.MakeButton("\u2715", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        root.AddChild(UIFactory.MakeSeparator());

        // Sign text
        var currentText = GetSignText();

        if (_isOwner)
        {
            // Editable for the owner
            _textEdit = UIFactory.MakeLineEdit(currentText, 15, 400f);
            _textEdit.Text = currentText;
            root.AddChild(_textEdit);

            var saveBtn = UIFactory.MakeButton("Save", 14, new Vector2(100, 36));
            saveBtn.Pressed += OnSave;
            root.AddChild(saveBtn);
        }
        else
        {
            // Read-only display for non-owners
            _textDisplay = UIFactory.MakeLabel(string.IsNullOrEmpty(currentText) ? "(blank)" : currentText, 15);
            _textDisplay.HorizontalAlignment = HorizontalAlignment.Center;
            _textDisplay.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _textDisplay.CustomMinimumSize = new Vector2(400, 0);
            root.AddChild(_textDisplay);
        }
    }

    private string GetSignText()
    {
        // SignText table not yet generated — return empty until server side is added.
        // Once available:
        // return GameManager.Instance.Conn?.Db.SignText.StructureId.Find(_structureId)?.Text ?? "";
        return "";
    }

    private void OnSave()
    {
        if (_textEdit == null) return;
        var text = _textEdit.Text;
        // SetSignText reducer not yet generated — log until server side is added.
        // GameManager.Instance.Conn?.Reducers.SetSignText(_structureId, text);
        GD.Print($"[SignPanel] Save text '{text}' for structure {_structureId} (reducer not yet wired)");
        UIManager.Instance.Pop();
    }
}
