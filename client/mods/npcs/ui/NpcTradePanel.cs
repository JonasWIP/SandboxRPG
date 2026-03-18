// client/mods/npcs/ui/NpcTradePanel.cs
using Godot;
using SpacetimeDB.Types;
using System.Linq;

namespace SandboxRPG;

public partial class NpcTradePanel : BasePanel
{
    private readonly ulong _npcId;
    private readonly string _npcType;
    private VBoxContainer _buyList = null!;
    private VBoxContainer _sellList = null!;
    private Label _currencyLabel = null!;

    public NpcTradePanel(ulong npcId, string npcType)
    {
        _npcId = npcId;
        _npcType = npcType;
    }

    public override void OnPushed()
    {
        base.OnPushed();
        GameManager.Instance.InventoryChanged += RefreshAll;
    }

    public override void OnPopped()
    {
        GameManager.Instance.InventoryChanged -= RefreshAll;
        base.OnPopped();
    }

    protected override void BuildUI()
    {
        var visual = NpcVisualRegistry.Get(_npcType);
        string name = visual?.DisplayName ?? _npcType;

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UIFactory.MakePanel(new Vector2(700, 500));
        center.AddChild(panel);

        var vbox = UIFactory.MakeVBox(10);
        panel.AddChild(vbox);

        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(titleRow);
        titleRow.AddChild(UIFactory.MakeTitle($"{name} - Shop", 20));
        var closeBtn = UIFactory.MakeButton("\u2715", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        vbox.AddChild(UIFactory.MakeSeparator());

        _currencyLabel = UIFactory.MakeLabel("", 14, UIFactory.ColourMuted);
        vbox.AddChild(_currencyLabel);

        // Two columns: Buy (left) and Sell (right)
        var columns = UIFactory.MakeHBox(16);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(columns);

        // Buy column
        var buyCol = UIFactory.MakeVBox(6);
        buyCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(buyCol);
        buyCol.AddChild(UIFactory.MakeLabel("Buy", 16, UIFactory.ColourAccent));
        var buyScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        buyCol.AddChild(buyScroll);
        _buyList = UIFactory.MakeVBox(4);
        buyScroll.AddChild(_buyList);

        // Sell column
        var sellCol = UIFactory.MakeVBox(6);
        sellCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(sellCol);
        sellCol.AddChild(UIFactory.MakeLabel("Sell", 16, UIFactory.ColourAccent));
        var sellScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        sellCol.AddChild(sellScroll);
        _sellList = UIFactory.MakeVBox(4);
        sellScroll.AddChild(_sellList);

        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshCurrency();
        PopulateBuyOffers();
        PopulateSellItems();
    }

    private void PopulateBuyOffers()
    {
        foreach (var child in _buyList.GetChildren())
            child.QueueFree();

        foreach (var offer in GameManager.Instance.GetTradeOffers(_npcType))
        {
            var row = UIFactory.MakeHBox(8);
            _buyList.AddChild(row);

            var itemDef = ItemRegistry.Get(offer.ItemType);
            string displayName = itemDef?.DisplayName ?? offer.ItemType.Replace('_', ' ');

            row.AddChild(UIFactory.MakeLabel(displayName, 13));
            row.AddChild(UIFactory.MakeLabel($"{offer.Price}c", 13, UIFactory.ColourMuted));

            var buyBtn = UIFactory.MakeButton("Buy", 11, new Vector2(50, 24));
            string itemType = offer.ItemType;
            buyBtn.Pressed += () => GameManager.Instance.TradeWithNpc(_npcId, itemType, 1);
            row.AddChild(buyBtn);
        }
    }

    private void PopulateSellItems()
    {
        foreach (var child in _sellList.GetChildren())
            child.QueueFree();

        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            if (item.ItemType == "copper_coin") continue; // don't sell currency

            var itemDef = ItemRegistry.Get(item.ItemType);
            string displayName = itemDef?.DisplayName ?? item.ItemType.Replace('_', ' ');

            // Calculate sell price: half of buy price if in trade offers, else 1
            int sellPrice = 1;
            foreach (var offer in GameManager.Instance.GetTradeOffers(_npcType))
            {
                if (offer.ItemType == item.ItemType)
                { sellPrice = System.Math.Max(1, offer.Price / 2); break; }
            }

            var row = UIFactory.MakeHBox(8);
            _sellList.AddChild(row);

            row.AddChild(UIFactory.MakeLabel($"{displayName} x{item.Quantity}", 13));
            row.AddChild(UIFactory.MakeLabel($"{sellPrice}c", 13, UIFactory.ColourMuted));

            var sellBtn = UIFactory.MakeButton("Sell", 11, new Vector2(50, 24));
            ulong itemId = item.Id;
            string dbgName = displayName;
            sellBtn.Pressed += () =>
            {
                GD.Print($"[NpcTradePanel] Selling itemId={itemId} ({dbgName}) to npc={_npcId}");
                GameManager.Instance.SellItemToNpc(_npcId, itemId, 1);
            };
            row.AddChild(sellBtn);
        }
    }

    private void RefreshCurrency()
    {
        uint coins = 0;
        foreach (var item in GameManager.Instance.GetMyInventory())
            if (item.ItemType == "copper_coin") coins += item.Quantity;
        if (_currencyLabel != null)
            _currencyLabel.Text = $"Your copper coins: {coins}";
    }
}
