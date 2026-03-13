using Godot;
using SandboxRPG;
using System.Collections.Generic;

/// <summary>
/// Combined Inventory + Crafting panel — opened with I or C.
/// Left half: item grid with right-click context menu (assign to hotbar slot, drop).
/// Right half: recipe list with Craft buttons.
/// Pushed onto UIManager stack; ESC or toggling I/C closes it.
/// </summary>
public partial class InventoryCraftingPanel : BasePanel
{
    private GridContainer _inventoryGrid = null!;
    private VBoxContainer _recipeList   = null!;
    private Control?      _contextMenu;

    // =========================================================================
    // BASE PANEL
    // =========================================================================

    public override void OnPushed()
    {
        GameManager.Instance.InventoryChanged  += Refresh;
        GameManager.Instance.RecipesLoaded     += Refresh;
        GameManager.Instance.SubscriptionApplied += Refresh;
        Refresh();
    }

    public override void OnPopped()
    {
        GameManager.Instance.InventoryChanged  -= Refresh;
        GameManager.Instance.RecipesLoaded     -= Refresh;
        GameManager.Instance.SubscriptionApplied -= Refresh;
    }

    protected override void BuildUI()
    {
        // Dim backdrop (but lighter — player can still see the world behind)
        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // Centred panel — wide enough for both columns
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var outerPanel = UIFactory.MakePanel(new Vector2(860, 560));
        center.AddChild(outerPanel);

        var root = UIFactory.MakeVBox(10);
        outerPanel.AddChild(root);

        // Title row
        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(titleRow);

        titleRow.AddChild(UIFactory.MakeTitle("Inventory & Crafting", 20));

        var closeBtn = UIFactory.MakeButton("✕", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        root.AddChild(UIFactory.MakeSeparator());

        // Two-column row
        var columns = UIFactory.MakeHBox(16);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        root.AddChild(columns);

        // ── LEFT: Inventory ──────────────────────────────────────────────────
        var leftCol = UIFactory.MakeVBox(8);
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(leftCol);

        leftCol.AddChild(UIFactory.MakeLabel("Inventory", 14, UIFactory.ColourAccent));

        var invScroll = new ScrollContainer();
        invScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        invScroll.CustomMinimumSize = new Vector2(0, 400);
        leftCol.AddChild(invScroll);

        _inventoryGrid = new GridContainer { Columns = 4 };
        _inventoryGrid.AddThemeConstantOverride("h_separation", 6);
        _inventoryGrid.AddThemeConstantOverride("v_separation", 6);
        invScroll.AddChild(_inventoryGrid);

        // ── RIGHT: Crafting ──────────────────────────────────────────────────
        var rightCol = UIFactory.MakeVBox(8);
        rightCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(rightCol);

        rightCol.AddChild(UIFactory.MakeLabel("Crafting", 14, UIFactory.ColourAccent));

        var craftScroll = new ScrollContainer();
        craftScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        craftScroll.CustomMinimumSize = new Vector2(0, 400);
        rightCol.AddChild(craftScroll);

        _recipeList = UIFactory.MakeVBox(6);
        craftScroll.AddChild(_recipeList);
    }

    // =========================================================================
    // REFRESH
    // =========================================================================

    private void Refresh()
    {
        RefreshInventory();
        RefreshRecipes();
    }

    private void RefreshInventory()
    {
        foreach (Node child in _inventoryGrid.GetChildren())
            child.QueueFree();

        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            var colour = ItemColour(item.ItemType);
            var btn = UIFactory.MakeSlotButton(item.ItemType, item.Quantity, colour);
            btn.CustomMinimumSize = UIFactory.SlotSize;

            // Capture for lambda
            ulong itemId  = item.Id;
            uint  itemQty = item.Quantity;

            btn.GuiInput += (evt) =>
            {
                if (evt is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                    ShowContextMenu(mb.GlobalPosition, itemId, itemQty);
            };

            _inventoryGrid.AddChild(btn);
        }
    }

    private void RefreshRecipes()
    {
        foreach (Node child in _recipeList.GetChildren())
            child.QueueFree();

        // Tally inventory for ingredient check
        var have = new Dictionary<string, uint>();
        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            have.TryGetValue(item.ItemType, out uint cur);
            have[item.ItemType] = cur + item.Quantity;
        }

        foreach (var recipe in GameManager.Instance.GetAllRecipes())
        {
            var row = UIFactory.MakeHBox(8);
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _recipeList.AddChild(row);

            // Recipe info column
            var infoCol = UIFactory.MakeVBox(2);
            infoCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(infoCol);

            infoCol.AddChild(UIFactory.MakeLabel(
                $"{recipe.ResultItemType.Replace('_', ' ')} ×{recipe.ResultQuantity}", 13));

            // Ingredients
            var ingredientStr = FormatIngredients(recipe.Ingredients);
            var ingLbl = UIFactory.MakeLabel(ingredientStr, 10, UIFactory.ColourMuted);
            infoCol.AddChild(ingLbl);

            // Can craft?
            bool canCraft = CanCraft(recipe.Ingredients, have);

            var craftBtn = UIFactory.MakeButton("Craft", 13, new Vector2(70, 34));
            craftBtn.Disabled = !canCraft;
            if (!canCraft)
                craftBtn.Modulate = new Color(1, 1, 1, 0.4f);

            ulong recipeId = recipe.Id;
            craftBtn.Pressed += () =>
            {
                GameManager.Instance.CraftRecipe(recipeId);
                // Refresh will fire via InventoryChanged signal
            };
            row.AddChild(craftBtn);

            _recipeList.AddChild(UIFactory.MakeSeparator());
        }
    }

    // =========================================================================
    // CONTEXT MENU
    // =========================================================================

    private void ShowContextMenu(Vector2 screenPos, ulong itemId, uint itemQty)
    {
        CloseContextMenu();

        _contextMenu = new Control();
        _contextMenu.SetAnchorsPreset(LayoutPreset.FullRect);
        _contextMenu.MouseFilter = MouseFilterEnum.Stop;

        // Close if clicking outside
        _contextMenu.GuiInput += (evt) =>
        {
            if (evt is InputEventMouseButton mb && mb.Pressed)
                CloseContextMenu();
        };

        var popup = new PanelContainer { Position = screenPos };
        _contextMenu.AddChild(popup);

        var col = UIFactory.MakeVBox(4);
        popup.AddChild(col);

        // Assign to hotbar slot header
        col.AddChild(UIFactory.MakeLabel("Assign to slot:", 11, UIFactory.ColourMuted));

        var slotRow = UIFactory.MakeHBox(4);
        col.AddChild(slotRow);

        for (int s = 0; s < Hotbar.SlotCount; s++)
        {
            int slot = s; // capture
            var slotBtn = UIFactory.MakeButton($"{s + 1}", 12, new Vector2(28, 28));
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

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static Color ItemColour(string itemType) => itemType switch
    {
        "wood"           => new Color("#8B5E3C"),
        "stone"          => new Color("#888899"),
        "iron"           => new Color("#AAAACC"),
        "wood_wall"      => new Color("#7B6B3D"),
        "stone_wall"     => new Color("#777788"),
        "wood_floor"     => new Color("#9B7040"),
        "stone_floor"    => new Color("#8888AA"),
        "wood_door"      => new Color("#6B500E"),
        "campfire"       => new Color("#E55454"),
        "workbench"      => new Color("#8B6040"),
        "chest"          => new Color("#B8862B"),
        "wood_pickaxe"   => new Color("#9B7040"),
        "stone_pickaxe"  => new Color("#777788"),
        "iron_pickaxe"   => new Color("#AAAACC"),
        "wood_axe"       => new Color("#8B5E3C"),
        _                => UIFactory.ColourMuted,
    };

    private static string FormatIngredients(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var parts = new List<string>();
        foreach (var part in raw.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2)
                parts.Add($"{kv[0].Trim().Replace('_', ' ')} ×{kv[1].Trim()}");
        }
        return string.Join("  ", parts);
    }

    private static bool CanCraft(string ingredients, Dictionary<string, uint> have)
    {
        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length != 2 || !uint.TryParse(kv[1], out uint need)) continue;
            string type = kv[0].Trim();
            have.TryGetValue(type, out uint owned);
            if (owned < need) return false;
        }
        return true;
    }
}
