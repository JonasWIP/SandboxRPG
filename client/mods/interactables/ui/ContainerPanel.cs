// client/mods/interactables/ui/ContainerPanel.cs
using Godot;

namespace SandboxRPG;

public partial class ContainerPanel : InteractionPanel
{
    private ContainerGrid _containerGrid = null!;
    private Label? _accessLabel;
    private readonly ulong _containerId;
    private readonly string _containerTable;
    private readonly int _slotCount;
    private readonly string _title;

    public ContainerPanel(ulong containerId, string containerTable, int slotCount, string title = "Chest")
    {
        _containerId = containerId;
        _containerTable = containerTable;
        _slotCount = slotCount;
        _title = title;
    }

    protected override string PanelTitle => _title;

    public override void OnPushed()
    {
        base.OnPushed();
        _inventoryGrid.ActiveContainerId = _containerId;
        _inventoryGrid.ActiveContainerTable = _containerTable;
        _inventoryGrid.ActiveContainerSlotCount = _slotCount;
        GameManager.Instance.ContainerSlotChanged += RefreshAll;
    }

    public override void OnPopped()
    {
        GameManager.Instance.ContainerSlotChanged -= RefreshAll;
        base.OnPopped();
    }

    protected override Control BuildContextSide()
    {
        var col = UIFactory.MakeVBox(8);

        _containerGrid = new ContainerGrid(_containerId, _containerTable, _slotCount, _title);
        col.AddChild(_containerGrid);

        // Access control toggle (owner only)
        var ac = GameManager.Instance.GetAccessControl(_containerId, _containerTable);
        if (ac is not null && GameManager.Instance.LocalIdentity is not null
            && ac.OwnerId == GameManager.Instance.LocalIdentity.Value)
        {
            col.AddChild(UIFactory.MakeSeparator());

            var accessRow = UIFactory.MakeHBox(8);
            accessRow.Alignment = BoxContainer.AlignmentMode.Center;
            col.AddChild(accessRow);

            _accessLabel = UIFactory.MakeLabel(
                ac.IsPublic ? "Public" : "Private", 12, UIFactory.ColourMuted);
            accessRow.AddChild(_accessLabel);

            var toggleBtn = UIFactory.MakeButton("Toggle Lock", 12, new Vector2(100, 28));
            toggleBtn.Pressed += () =>
            {
                GameManager.Instance.ToggleAccess(_containerId, _containerTable);
            };
            accessRow.AddChild(toggleBtn);
        }

        return col;
    }

    protected override void RefreshContextSide()
    {
        _containerGrid?.Refresh();

        // Update access label
        if (_accessLabel != null)
        {
            var ac = GameManager.Instance.GetAccessControl(_containerId, _containerTable);
            if (ac is not null)
                _accessLabel.Text = ac.IsPublic ? "Public" : "Private";
        }
    }
}
