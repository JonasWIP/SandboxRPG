// mods/npcs/server/NpcsMod.cs
using SpacetimeDB;
using System;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly NpcsModImpl _npcsMod = new();

    private sealed class NpcsModImpl : IMod
    {
        public NpcsModImpl() => ModLoader.Register(this);

        public string   Name         => "npcs";
        public string   Version      => "1.0.0";
        public string[] Dependencies => new[] { "base" };

        public void Seed(ReducerContext ctx)
        {
            SeedNpcConfigs(ctx);
            SeedLootTables(ctx);
            SeedSpawnRules(ctx);
            SeedTradeOffers(ctx);
            SeedNpcItems(ctx);
            Log.Info("[NpcsMod] Seeded.");
        }
    }

    private static void SeedNpcConfigs(ReducerContext ctx)
    {
        ctx.Db.NpcConfig.Insert(new NpcConfig
        {
            NpcType = "wolf", MaxHealth = 50, AttackDamage = 8,
            AttackRange = 2.0f, AttackCooldownMs = 1500,
            IsAttackable = true, IsTrader = false, HasDialogue = false,
        });
        ctx.Db.NpcConfig.Insert(new NpcConfig
        {
            NpcType = "merchant", MaxHealth = 100, AttackDamage = 0,
            AttackRange = 0f, AttackCooldownMs = 0,
            IsAttackable = false, IsTrader = true, HasDialogue = true,
        });
        ctx.Db.NpcConfig.Insert(new NpcConfig
        {
            NpcType = "guard", MaxHealth = 150, AttackDamage = 15,
            AttackRange = 2.5f, AttackCooldownMs = 1200,
            IsAttackable = true, IsTrader = false, HasDialogue = true,
        });
    }

    private static void SeedLootTables(ReducerContext ctx)
    {
        ctx.Db.NpcLootTable.Insert(new NpcLootTable { NpcType = "wolf", ItemType = "raw_meat",  Quantity = 2, DropChance = 1.0f });
        ctx.Db.NpcLootTable.Insert(new NpcLootTable { NpcType = "wolf", ItemType = "wolf_pelt", Quantity = 1, DropChance = 0.5f });
    }

    private static void SeedSpawnRules(ReducerContext ctx)
    {
        // Wolves spawn in a zone 30 units from center
        ctx.Db.NpcSpawnRule.Insert(new NpcSpawnRule
        {
            NpcType = "wolf", ZoneX = 30f, ZoneZ = 30f,
            ZoneRadius = 15f, MaxCount = 3, RespawnTimeSec = 30f,
        });
        // Merchant near spawn
        ctx.Db.NpcSpawnRule.Insert(new NpcSpawnRule
        {
            NpcType = "merchant", ZoneX = 5f, ZoneZ = -5f,
            ZoneRadius = 0f, MaxCount = 1, RespawnTimeSec = 10f,
        });
        // Guard near spawn
        ctx.Db.NpcSpawnRule.Insert(new NpcSpawnRule
        {
            NpcType = "guard", ZoneX = -5f, ZoneZ = -5f,
            ZoneRadius = 0f, MaxCount = 1, RespawnTimeSec = 10f,
        });
    }

    private static void SeedTradeOffers(ReducerContext ctx)
    {
        ctx.Db.NpcTradeOffer.Insert(new NpcTradeOffer { NpcType = "merchant", ItemType = "bread",         Price = 5,  Currency = "copper_coin" });
        ctx.Db.NpcTradeOffer.Insert(new NpcTradeOffer { NpcType = "merchant", ItemType = "iron_sword",    Price = 20, Currency = "copper_coin" });
        ctx.Db.NpcTradeOffer.Insert(new NpcTradeOffer { NpcType = "merchant", ItemType = "health_potion", Price = 10, Currency = "copper_coin" });
    }

    private static void SeedNpcItems(ReducerContext ctx)
    {
        // Register new item recipes so they can exist in inventory
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "iron_sword", ResultQuantity = 1,
            Ingredients = "iron:5,wood:2", CraftTimeSeconds = 4f, Station = "crafting_table",
        });
    }
}
