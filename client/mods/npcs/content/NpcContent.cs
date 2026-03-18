// client/mods/npcs/content/NpcContent.cs
using Godot;

namespace SandboxRPG;

public static class NpcContent
{
    public static void RegisterAll()
    {
        RegisterVisuals();
        RegisterDialogues();
        RegisterItems();
    }

    private static void RegisterVisuals()
    {
        NpcVisualRegistry.Register("wolf", new NpcVisualDef
        {
            DisplayName = "Wolf", Scale = 0.5f,
            TintColor = new Color(0.5f, 0.5f, 0.5f),
            HealthBarColor = Colors.Red,
        });
        NpcVisualRegistry.Register("merchant", new NpcVisualDef
        {
            DisplayName = "Merchant", Scale = 1.0f,
            TintColor = new Color(0.2f, 0.7f, 0.3f),
            HealthBarColor = Colors.Green,
        });
        NpcVisualRegistry.Register("guard", new NpcVisualDef
        {
            DisplayName = "Guard", Scale = 1.1f,
            TintColor = new Color(0.3f, 0.4f, 0.8f),
            HealthBarColor = Colors.Blue,
        });
    }

    private static void RegisterDialogues()
    {
        DialogueRegistry.Register("merchant", new[]
        {
            "Welcome, traveler!",
            "Browse my wares.",
            "Only the finest goods here.",
        });
        DialogueRegistry.Register("guard", new[]
        {
            "Move along, citizen.",
            "The town is safe under my watch.",
            "Report any suspicious activity.",
        });
    }

    private static void RegisterItems()
    {
        // New items for NPC system
        ItemRegistry.Register("iron_sword",    new ItemDef { DisplayName = "Iron Sword",    MaxStack = 1,  Scale = 0.3f });
        ItemRegistry.Register("health_potion", new ItemDef { DisplayName = "Health Potion", MaxStack = 10, Scale = 0.25f, TintColor = new Color(0.9f, 0.2f, 0.2f) });
        ItemRegistry.Register("raw_meat",      new ItemDef { DisplayName = "Raw Meat",      MaxStack = 20, Scale = 0.25f, TintColor = new Color(0.8f, 0.3f, 0.3f) });
        ItemRegistry.Register("wolf_pelt",     new ItemDef { DisplayName = "Wolf Pelt",     MaxStack = 10, Scale = 0.3f,  TintColor = new Color(0.6f, 0.5f, 0.4f) });
        ItemRegistry.Register("bread",         new ItemDef { DisplayName = "Bread",         MaxStack = 20, Scale = 0.25f, TintColor = new Color(0.9f, 0.8f, 0.5f) });
    }
}
