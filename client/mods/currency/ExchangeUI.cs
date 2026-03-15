#if MOD_CURRENCY
using Godot;
using SandboxRPG;

/// <summary>
/// Exchange machine UI — lets players convert resources to Copper
/// or withdraw/deposit physical coin items.
/// </summary>
public partial class ExchangeUI : Node
{
    public static void Open(ulong id)
    {
        var layer = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(300, 280);
        panel.Position = new Vector2(-150, -140);

        var vbox = new VBoxContainer { Position = new Vector2(10, 10), Size = new Vector2(280, 260) };
        vbox.AddChild(new Label { Text = "Exchange Machine" });

        // Exchange resources
        vbox.AddChild(new Label { Text = "--- Exchange Resources ---" });
        var resType = new OptionButton();
        resType.AddItem("wood (10 → 5Cu)");
        resType.AddItem("stone (5 → 5Cu)");
        resType.AddItem("iron (1 → 20Cu)");
        vbox.AddChild(resType);
        var resSpin = new SpinBox { MinValue = 1, MaxValue = 9999, Value = 10, Step = 1 };
        vbox.AddChild(resSpin);
        var exchangeBtn = new Button { Text = "Exchange Resources" };
        exchangeBtn.Pressed += () => {
            string[] types = { "wood", "stone", "iron" };
            GameManager.Instance.Conn.Reducers.ExchangeResources(types[resType.Selected], (uint)resSpin.Value);
        };
        vbox.AddChild(exchangeBtn);

        // Withdraw coins
        vbox.AddChild(new Label { Text = "--- Withdraw Coins ---" });
        var denom = new OptionButton();
        denom.AddItem("copper (1Cu each)");
        denom.AddItem("silver (100Cu each)");
        denom.AddItem("gold (10000Cu each)");
        vbox.AddChild(denom);
        var withdrawSpin = new SpinBox { MinValue = 1, MaxValue = 1000, Value = 1, Step = 1 };
        vbox.AddChild(withdrawSpin);
        var withdrawBtn = new Button { Text = "Withdraw" };
        withdrawBtn.Pressed += () => {
            string[] denoms = { "copper", "silver", "gold" };
            GameManager.Instance.Conn.Reducers.WithdrawCoins(denoms[denom.Selected], (uint)withdrawSpin.Value);
        };
        vbox.AddChild(withdrawBtn);

        // Close button
        var closeBtn = new Button { Text = "Close" };
        closeBtn.Pressed += () => layer.QueueFree();
        vbox.AddChild(closeBtn);

        panel.AddChild(vbox);
        layer.AddChild(panel);
        var tree = ((Node)GameManager.Instance).GetTree();
        tree.Root.AddChild(layer);
    }
}
#endif
