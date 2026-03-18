// client/mods/interactables/ui/SignPanel.cs
using Godot;

namespace SandboxRPG;

public partial class SignPanel : BasePanel
{
    private readonly ulong _structureId;

    public SignPanel(ulong structureId)
    {
        _structureId = structureId;
    }

    protected override void BuildUI()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var outerPanel = UIFactory.MakePanel(new Vector2(400, 300));
        center.AddChild(outerPanel);

        var root = UIFactory.MakeVBox(10);
        outerPanel.AddChild(root);

        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(titleRow);
        titleRow.AddChild(UIFactory.MakeTitle("Sign", 20));

        var closeBtn = UIFactory.MakeButton("\u2715", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        root.AddChild(UIFactory.MakeSeparator());

        // Determine ownership
        bool isOwner = false;
        var ac = GameManager.Instance.GetAccessControl(_structureId, "placed_structure");
        if (ac is not null && GameManager.Instance.LocalIdentity is not null)
            isOwner = ac.OwnerId == GameManager.Instance.LocalIdentity.Value;

        // Get current text
        string currentText = "";
        if (GameManager.Instance.Conn != null)
        {
            var st = GameManager.Instance.Conn.Db.SignText.StructureId.Find(_structureId);
            if (st is not null) currentText = st.Text;
        }

        if (isOwner)
        {
            var textEdit = new TextEdit
            {
                Text = currentText,
                CustomMinimumSize = new Vector2(350, 150),
            };
            textEdit.AddThemeFontSizeOverride("font_size", 14);
            root.AddChild(textEdit);

            var saveBtn = UIFactory.MakeButton("Save", 14, new Vector2(100, 34));
            saveBtn.Pressed += () =>
            {
                GameManager.Instance.Conn?.Reducers.UpdateSignText(_structureId, textEdit.Text);
                UIManager.Instance.Pop();
            };
            root.AddChild(saveBtn);
        }
        else
        {
            var label = UIFactory.MakeLabel(
                string.IsNullOrEmpty(currentText) ? "(empty)" : currentText, 14);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(350, 150);
            root.AddChild(label);
        }
    }
}
