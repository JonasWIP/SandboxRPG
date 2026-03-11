using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG;

/// <summary>
/// Inventory panel: toggled with I key.
/// Shows all items the player owns, with options to drop or move to hotbar.
/// </summary>
public partial class InventoryUI : Control
{
    private PanelContainer _panel = null!;
    private VBoxContainer _itemList = null!;
    private bool _isOpen;

    public override void _Ready()
    {
        // Main panel (center of screen)
        _panel = new PanelContainer
        {
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.Center,
            OffsetLeft = -200,
            OffsetRight = 200,
            OffsetTop = -250,
            OffsetBottom = 250,
            Visible = false,
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.1f, 0.15f, 0.9f),
            BorderColor = new Color(0.4f, 0.4f, 0.5f),
            BorderWidthBottom = 2,
            BorderWidthTop = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", style);
        AddChild(_panel);

        var mainVbox = new VBoxContainer();
        _panel.AddChild(mainVbox);

        // Title
        var title = new Label
        {
            Text = "INVENTORY",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", new Color(1, 0.85f, 0.3f));
        title.AddThemeFontSizeOverride("font_size", 20);
        mainVbox.AddChild(title);

        mainVbox.AddChild(new HSeparator());

        // Scrollable item list
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        mainVbox.AddChild(scroll);

        _itemList = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_itemList);

        // Connect
        GameManager.Instance.InventoryChanged += RefreshInventory;
        GameManager.Instance.SubscriptionApplied += RefreshInventory;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("inventory"))
        {
            ToggleInventory();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ToggleInventory()
    {
        _isOpen = !_isOpen;
        _panel.Visible = _isOpen;

        if (_isOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            RefreshInventory();
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void RefreshInventory()
    {
        if (!_isOpen) return;

        // Clear old entries
        foreach (var child in _itemList.GetChildren())
        {
            child.QueueFree();
        }

        var items = GameManager.Instance.GetMyInventory().ToList();

        if (items.Count == 0)
        {
            var empty = new Label { Text = "No items" };
            empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _itemList.AddChild(empty);
            return;
        }

        foreach (var item in items)
        {
            var row = new HBoxContainer();

            // Item icon (colored box placeholder)
            var iconColor = GetItemColor(item.ItemType);
            var icon = new ColorRect
            {
                CustomMinimumSize = new Vector2(24, 24),
                Color = iconColor,
            };
            row.AddChild(icon);

            // Item name & quantity
            var label = new Label
            {
                Text = $"  {FormatItemName(item.ItemType)} x{item.Quantity}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
            label.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(label);

            // Slot info
            if (item.Slot >= 0)
            {
                var slotLabel = new Label { Text = $"[Slot {item.Slot + 1}]" };
                slotLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.8f, 1f));
                slotLabel.AddThemeFontSizeOverride("font_size", 12);
                row.AddChild(slotLabel);
            }

            // Drop button
            var dropBtn = new Button
            {
                Text = "Drop",
                CustomMinimumSize = new Vector2(60, 0),
            };
            var itemId = item.Id;
            var qty = item.Quantity;
            dropBtn.Pressed += () =>
            {
                GameManager.Instance.DropInventoryItem(itemId, 1);
            };
            row.AddChild(dropBtn);

            _itemList.AddChild(row);
        }
    }

    private static string FormatItemName(string itemType)
    {
        return itemType.Replace("_", " ").ToUpper();
    }

    private static Color GetItemColor(string itemType)
    {
        return itemType switch
        {
            "wood" or "wood_pickaxe" or "wood_axe" => new Color(0.6f, 0.4f, 0.2f),
            "stone" or "stone_pickaxe" => new Color(0.5f, 0.5f, 0.55f),
            "iron" or "iron_pickaxe" => new Color(0.7f, 0.7f, 0.75f),
            "wood_wall" or "wood_floor" or "wood_door" => new Color(0.7f, 0.5f, 0.3f),
            "stone_wall" or "stone_floor" => new Color(0.6f, 0.6f, 0.65f),
            "campfire" => new Color(0.9f, 0.4f, 0.1f),
            "workbench" => new Color(0.5f, 0.35f, 0.2f),
            "chest" => new Color(0.55f, 0.4f, 0.25f),
            _ => new Color(0.8f, 0.8f, 0.2f),
        };
    }
}
