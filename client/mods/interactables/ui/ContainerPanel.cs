// client/mods/interactables/ui/ContainerPanel.cs
using Godot;

namespace SandboxRPG;

public partial class ContainerPanel : InteractionPanel
{
    private ContainerGrid _containerGrid = null!;
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
        // Enable deposit actions in inventory grid
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
        _containerGrid = new ContainerGrid(_containerId, _containerTable, _slotCount, _title);
        return _containerGrid;
    }

    protected override void RefreshContextSide() => _containerGrid?.Refresh();
}
