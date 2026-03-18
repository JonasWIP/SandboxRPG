using Godot;

namespace SandboxRPG;

public partial class ContainerGrid : VBoxContainer
{
    private GridContainer _grid = null!;
    private ulong _containerId;
    private string _containerTable = "";
    private int _slotCount;
    private string _title = "Container";

    public ContainerGrid(ulong containerId, string containerTable, int slotCount, string title = "Container")
    {
        _containerId = containerId;
        _containerTable = containerTable;
        _slotCount = slotCount;
        _title = title;
    }

    public override void _Ready()
    {
        AddChild(UIFactory.MakeLabel(_title, 14, UIFactory.ColourAccent));

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 400);
        AddChild(scroll);

        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_grid);
    }

    public void Refresh()
    {
        foreach (Node child in _grid.GetChildren())
            child.QueueFree();

        var slotMap = new (string itemType, uint quantity, ulong slotId)?[_slotCount];
        foreach (var cs in GameManager.Instance.GetContainerSlots(_containerId))
        {
            if (cs.Slot >= 0 && cs.Slot < _slotCount)
                slotMap[cs.Slot] = (cs.ItemType, cs.Quantity, cs.Id);
        }

        for (int i = 0; i < _slotCount; i++)
        {
            int slot = i;
            if (slotMap[i] is var (itemType, qty, slotId) && !string.IsNullOrEmpty(itemType) && qty > 0)
            {
                var btn = UIFactory.MakeSlotButton(itemType, qty, UIFactory.ColourMuted);
                btn.CustomMinimumSize = UIFactory.SlotSize;
                btn.Pressed += () =>
                {
                    GameManager.Instance.ContainerWithdraw(_containerId, _containerTable, slot, qty);
                };
                _grid.AddChild(btn);
            }
            else
            {
                var btn = UIFactory.MakeButton("", 10, UIFactory.SlotSize);
                btn.CustomMinimumSize = UIFactory.SlotSize;
                _grid.AddChild(btn);
            }
        }
    }

    public ulong ContainerId => _containerId;
    public string ContainerTable => _containerTable;
}
