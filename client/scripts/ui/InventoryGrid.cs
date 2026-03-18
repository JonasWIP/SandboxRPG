using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

public partial class InventoryGrid : VBoxContainer
{
    private GridContainer _grid = null!;
    private Control? _contextMenu;

    public override void _Ready()
    {
        var header = UIFactory.MakeLabel("Inventory", 14, UIFactory.ColourAccent);
        AddChild(header);

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

        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            var colour = ItemColour(item.ItemType);
            var btn = UIFactory.MakeSlotButton(item.ItemType, item.Quantity, colour);
            btn.CustomMinimumSize = UIFactory.SlotSize;

            ulong itemId = item.Id;
            uint itemQty = item.Quantity;

            btn.GuiInput += (evt) =>
            {
                if (evt is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                    ShowContextMenu(mb.GlobalPosition, itemId, itemQty);
            };

            _grid.AddChild(btn);
        }
    }

    private void ShowContextMenu(Vector2 screenPos, ulong itemId, uint itemQty)
    {
        CloseContextMenu();

        _contextMenu = new Control();
        _contextMenu.SetAnchorsPreset(LayoutPreset.FullRect);
        _contextMenu.MouseFilter = MouseFilterEnum.Stop;
        _contextMenu.GuiInput += (evt) =>
        {
            if (evt is InputEventMouseButton mb && mb.Pressed)
                CloseContextMenu();
        };

        var popup = new PanelContainer { Position = screenPos };
        _contextMenu.AddChild(popup);

        var col = UIFactory.MakeVBox(4);
        popup.AddChild(col);

        var occupiedSlots = new Dictionary<int, string>();
        foreach (var inv in GameManager.Instance.GetMyInventory())
            if (inv.Slot >= 0 && inv.Slot < Hotbar.SlotCount)
                occupiedSlots[inv.Slot] = inv.ItemType;

        int currentSlot = -1;
        foreach (var inv in GameManager.Instance.GetMyInventory())
            if (inv.Id == itemId) { currentSlot = inv.Slot; break; }

        col.AddChild(UIFactory.MakeLabel("Assign to slot:", 11, UIFactory.ColourMuted));

        var slotRow = UIFactory.MakeHBox(4);
        col.AddChild(slotRow);

        for (int s = 0; s < Hotbar.SlotCount; s++)
        {
            int slot = s;
            bool isCurrent = slot == currentSlot;
            bool isOccupied = occupiedSlots.ContainsKey(slot) && !isCurrent;

            var slotBtn = UIFactory.MakeButton($"{s + 1}", 12, new Vector2(28, 28));
            if (isCurrent)
                slotBtn.Modulate = UIFactory.ColourAccent;
            else if (isOccupied)
                slotBtn.Modulate = new Color(1f, 0.6f, 0.3f);

            slotBtn.Pressed += () =>
            {
                GameManager.Instance.MoveItemSlot(itemId, slot);
                CloseContextMenu();
            };
            slotRow.AddChild(slotBtn);
        }

        col.AddChild(UIFactory.MakeSeparator());

        var drop1Btn = UIFactory.MakeButton("Drop 1", 13, new Vector2(120, 32));
        drop1Btn.Pressed += () =>
        {
            GameManager.Instance.DropInventoryItem(itemId, 1);
            CloseContextMenu();
        };
        col.AddChild(drop1Btn);

        if (itemQty > 1)
        {
            var dropAllBtn = UIFactory.MakeButton($"Drop All ({itemQty})", 13, new Vector2(120, 32));
            dropAllBtn.Pressed += () =>
            {
                GameManager.Instance.DropInventoryItem(itemId, itemQty);
                CloseContextMenu();
            };
            col.AddChild(dropAllBtn);
        }

        AddChild(_contextMenu);
    }

    private void CloseContextMenu()
    {
        _contextMenu?.QueueFree();
        _contextMenu = null;
    }

    private static Color ItemColour(string itemType) => itemType switch
    {
        "wood" => new Color("#8B5E3C"),
        "stone" => new Color("#888899"),
        "iron" => new Color("#AAAACC"),
        "wood_wall" => new Color("#7B6B3D"),
        "stone_wall" => new Color("#777788"),
        "wood_floor" => new Color("#9B7040"),
        "stone_floor" => new Color("#8888AA"),
        "wood_door" => new Color("#6B500E"),
        "campfire" => new Color("#E55454"),
        "workbench" => new Color("#8B6040"),
        "chest" => new Color("#B8862B"),
        "wood_pickaxe" => new Color("#9B7040"),
        "stone_pickaxe" => new Color("#777788"),
        "iron_pickaxe" => new Color("#AAAACC"),
        "wood_axe" => new Color("#8B5E3C"),
        _ => UIFactory.ColourMuted,
    };
}
