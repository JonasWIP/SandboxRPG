using Godot;

namespace SandboxRPG;

public partial class CurrencyClientMod : Node, IClientMod
{
    public string ModName => "currency";
    public string[] Dependencies => new[] { "base" };

    public override void _Ready() => ModManager.Register(this);

    public void Initialize(Node sceneRoot)
    {
        ItemRegistry.Register("copper_coin", new ItemDef
        {
            DisplayName = "Copper Coin",
            TintColor = new Color("#B87333"),
            MaxStack = 100,
        });
        ItemRegistry.Register("silver_coin", new ItemDef
        {
            DisplayName = "Silver Coin",
            TintColor = new Color("#C0C0C0"),
            MaxStack = 100,
        });
        ItemRegistry.Register("gold_coin", new ItemDef
        {
            DisplayName = "Gold Coin",
            TintColor = new Color("#FFD700"),
            MaxStack = 100,
        });
        ItemRegistry.Register("platinum_coin", new ItemDef
        {
            DisplayName = "Platinum Coin",
            TintColor = new Color("#E5E4E2"),
            MaxStack = 100,
        });
    }
}
