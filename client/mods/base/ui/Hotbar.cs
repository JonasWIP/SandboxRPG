using Godot;
using SandboxRPG;
using System.Collections.Generic;

/// <summary>
/// Always-visible 8-slot hotbar at the bottom-centre of the screen.
/// Not a BasePanel — lives as a permanent child of HUD.
/// Reads inventory items where Slot 0–7 from SpacetimeDB.
/// Keys 1–8 change the active slot.
/// </summary>
public partial class Hotbar : Control
{
    public const int SlotCount = 8;

    private int _activeSlot = 0;
    private readonly PanelContainer[] _slotPanels = new PanelContainer[SlotCount];
    private readonly Label[]          _nameLabels  = new Label[SlotCount];
    private readonly Label[]          _qtyLabels   = new Label[SlotCount];
    private readonly ColorRect[]      _colourDots  = new ColorRect[SlotCount];

    /// <summary>Item type currently in the active hotbar slot, or null if empty.</summary>
    public static Hotbar? Instance { get; private set; }

    public string? ActiveItemType { get; private set; }

    // =========================================================================
    // GODOT LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        Instance = this;

        // Explicitly size to the viewport bottom — anchor-based sizing is unreliable
        // when the parent Control's layout hasn't been finalised at _Ready() time.
        var vp = GetViewport().GetVisibleRect();
        float totalH = UIFactory.SlotSize.Y + 30; // slot + number label above + padding
        Size = new Vector2(vp.Size.X, totalH);
        Position = new Vector2(0, vp.Size.Y - totalH);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildUI();

        GameManager.Instance.InventoryChanged += Refresh;
        GameManager.Instance.SubscriptionApplied += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (GameManager.Instance is not null)
        {
            GameManager.Instance.InventoryChanged -= Refresh;
            GameManager.Instance.SubscriptionApplied -= Refresh;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Keys 1–8 change active slot
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            int slot = key.PhysicalKeycode switch
            {
                Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3,
                Key.Key5 => 4, Key.Key6 => 5, Key.Key7 => 6, Key.Key8 => 7,
                _ => -1,
            };
            if (slot >= 0)
            {
                SetActiveSlot(slot);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Scroll wheel cycles slots (only when no UI panels open)
        if (@event is InputEventMouseButton mb && mb.Pressed && !UIManager.Instance.IsAnyOpen)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                SetActiveSlot((_activeSlot - 1 + SlotCount) % SlotCount);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                SetActiveSlot((_activeSlot + 1) % SlotCount);
        }
    }

    // =========================================================================
    // UI CONSTRUCTION
    // =========================================================================

    private void BuildUI()
    {
        // Full-width row — parent Hotbar is explicitly sized, so FullRect works here
        var center = new HBoxContainer();
        center.AddThemeConstantOverride("separation", 4);
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        // Spacer to push slots to centre
        var spacerL = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        center.AddChild(spacerL);

        for (int i = 0; i < SlotCount; i++)
        {
            var wrapper = new VBoxContainer();
            wrapper.AddThemeConstantOverride("separation", 2);
            center.AddChild(wrapper);

            // Number label above slot
            var numLbl = UIFactory.MakeLabel($"{i + 1}", 10, UIFactory.ColourMuted);
            numLbl.HorizontalAlignment = HorizontalAlignment.Center;
            wrapper.AddChild(numLbl);

            // Slot panel
            var slot = UIFactory.MakeHotbarSlot(i == _activeSlot);
            _slotPanels[i] = slot;
            wrapper.AddChild(slot);

            var inner = UIFactory.MakeVBox(0);
            inner.CustomMinimumSize = UIFactory.SlotSize;
            slot.AddChild(inner);

            var dot = new ColorRect
            {
                Color = UIFactory.ColourMuted,
                CustomMinimumSize = new Vector2(8, 8),
                Visible = false,
            };
            _colourDots[i] = dot;

            var nameLbl = UIFactory.MakeLabel("", 9, UIFactory.ColourText);
            nameLbl.HorizontalAlignment = HorizontalAlignment.Center;
            nameLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            nameLbl.CustomMinimumSize = new Vector2(UIFactory.SlotSize.X - 4, 0);
            _nameLabels[i] = nameLbl;

            var qtyLbl = UIFactory.MakeLabel("", 9, UIFactory.ColourMuted);
            qtyLbl.HorizontalAlignment = HorizontalAlignment.Right;
            _qtyLabels[i] = qtyLbl;

            inner.AddChild(dot);
            inner.AddChild(nameLbl);
            inner.AddChild(qtyLbl);

            // Item-name tooltip label below slot
            var tipLbl = UIFactory.MakeLabel("", 10, UIFactory.ColourMuted);
            tipLbl.HorizontalAlignment = HorizontalAlignment.Center;
            wrapper.AddChild(tipLbl);

            // Store tip label index so Refresh can update it
            slot.SetMeta("tip_label", tipLbl.GetInstanceId());
        }

        var spacerR = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        center.AddChild(spacerR);
    }

    // =========================================================================
    // DATA
    // =========================================================================

    private void Refresh()
    {
        // Build slot-index → item map
        var slotMap = new Dictionary<int, (string type, uint qty, string color)>();
        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            if (item.Slot >= 0 && item.Slot < SlotCount)
                slotMap[item.Slot] = (item.ItemType, item.Quantity, "#AAAAAA");
        }

        for (int i = 0; i < SlotCount; i++)
        {
            bool hasItem = slotMap.TryGetValue(i, out var info);
            _nameLabels[i].Text  = hasItem ? info.type.Replace('_', ' ') : "";
            _qtyLabels[i].Text   = hasItem && info.qty > 1 ? $"×{info.qty}" : "";
            _colourDots[i].Visible = hasItem;
        }

        // Update active item type
        ActiveItemType = slotMap.TryGetValue(_activeSlot, out var active) ? active.type : null;
    }

    // =========================================================================
    // ACTIVE SLOT
    // =========================================================================

    private void SetActiveSlot(int index)
    {
        int prev = _activeSlot;
        _activeSlot = index;

        UpdateSlotStyle(prev, false);
        UpdateSlotStyle(index, true);

        // Update active item — scan directly, no intermediate dictionary needed
        ActiveItemType = null;
        foreach (var item in GameManager.Instance.GetMyInventory())
            if (item.Slot == _activeSlot) { ActiveItemType = item.ItemType; break; }
    }

    private void UpdateSlotStyle(int index, bool active)
    {
        var style = new StyleBoxFlat
        {
            BgColor     = UIFactory.ColourSlotBg,
            BorderWidthBottom = 2, BorderWidthTop = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderColor = active ? UIFactory.ColourSlotActive : UIFactory.ColourSlotBorder,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        _slotPanels[index].AddThemeStyleboxOverride("panel", style);
    }
}
