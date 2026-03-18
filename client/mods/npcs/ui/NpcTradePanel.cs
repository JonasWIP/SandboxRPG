// client/mods/npcs/ui/NpcTradePanel.cs
using Godot;
using SpacetimeDB.Types;
using System.Linq;

namespace SandboxRPG;

public partial class NpcTradePanel : BasePanel
{
    private readonly ulong _npcId;
    private readonly string _npcType;
    private VBoxContainer _offerList = null!;
    private Label _currencyLabel = null!;

    public NpcTradePanel(ulong npcId, string npcType)
    {
        _npcId = npcId;
        _npcType = npcType;
    }

    public override void OnPushed()
    {
        base.OnPushed();
        GameManager.Instance.InventoryChanged += RefreshCurrency;
    }

    public override void OnPopped()
    {
        GameManager.Instance.InventoryChanged -= RefreshCurrency;
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

        var panel = UIFactory.MakePanel(new Vector2(500, 400));
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

        // Currency display
        _currencyLabel = UIFactory.MakeLabel("", 14, UIFactory.ColourMuted);
        vbox.AddChild(_currencyLabel);
        RefreshCurrency();

        // Offers scroll
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        vbox.AddChild(scroll);

        _offerList = UIFactory.MakeVBox(6);
        scroll.AddChild(_offerList);

        PopulateOffers();
    }

    private void PopulateOffers()
    {
        foreach (var child in _offerList.GetChildren())
            child.QueueFree();

        foreach (var offer in GameManager.Instance.GetTradeOffers(_npcType))
        {
            var row = UIFactory.MakeHBox(10);
            _offerList.AddChild(row);

            var itemDef = ItemRegistry.Get(offer.ItemType);
            string displayName = itemDef?.DisplayName ?? offer.ItemType.Replace('_', ' ');

            row.AddChild(UIFactory.MakeLabel(displayName, 14));
            row.AddChild(UIFactory.MakeLabel($"{offer.Price} {offer.Currency.Replace('_', ' ')}", 14, UIFactory.ColourMuted));

            var buyBtn = UIFactory.MakeButton("Buy", 12, new Vector2(60, 28));
            string itemType = offer.ItemType;
            buyBtn.Pressed += () => GameManager.Instance.TradeWithNpc(_npcId, itemType, 1);
            row.AddChild(buyBtn);
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
