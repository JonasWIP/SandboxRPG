// mods/base/server/BaseMod.cs
using SpacetimeDB;
using System;
using System.Collections.Generic;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    // Registers BaseMod with the loader when the module class is initialised.
    private static readonly BaseModImpl _baseMod = new();

    private sealed class BaseModImpl : IMod
    {
        public BaseModImpl() => ModLoader.Register(this);

        public string   Name         => "base";
        public string   Version      => "1.0.0";
        public string[] Dependencies => Array.Empty<string>();

        public void Seed(ReducerContext ctx)
        {
            RegisterStructures();
            RegisterHarvest();
            SeedTerrainConfig(ctx);
            SeedRecipes(ctx);
            SeedWorldItems(ctx);
            SeedWorldObjects(ctx);
            Log.Info("[BaseMod] Seeded.");
        }

        public void OnClientConnected(ReducerContext ctx, Identity identity)
        {
            // Implemented in BaseMod — see bottom of this file.
            BaseModHandleClientConnected(ctx, identity);
        }

        public void OnClientDisconnected(ReducerContext ctx, Identity identity)
        {
            BaseModHandleClientDisconnected(ctx, identity);
        }
    }

    // ---- registry setup (populated during Seed) ----------------------------

    private static void RegisterStructures()
    {
        StructureConfig.Register("wood_wall",   100f);
        StructureConfig.Register("stone_wall",  250f);
        StructureConfig.Register("wood_floor",   80f);
        StructureConfig.Register("stone_floor", 200f);
        StructureConfig.Register("wood_door",    60f);
        StructureConfig.Register("campfire",     50f);
        StructureConfig.Register("workbench",   100f);
        StructureConfig.Register("chest",        80f);
    }

    private static void RegisterHarvest()
    {
        // Tool damage
        HarvestConfig.RegisterToolDamage("wood_axe",      "tree_pine",  34);
        HarvestConfig.RegisterToolDamage("wood_axe",      "tree_dead",  34);
        HarvestConfig.RegisterToolDamage("wood_axe",      "tree_palm",  34);
        HarvestConfig.RegisterToolDamage("wood_axe",      "bush",       34);
        HarvestConfig.RegisterToolDamage("wood_pickaxe",  "rock_large", 34);
        HarvestConfig.RegisterToolDamage("wood_pickaxe",  "rock_small", 34);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "rock_large", 50);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "rock_small", 50);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "rock_large", 75);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "rock_small", 75);

        // Off-primary damage — preserves original switch semantics exactly:
        // stone_pickaxe on trees = 8, iron_pickaxe on trees = 10, axe on rocks = 5
        HarvestConfig.RegisterToolDamage("wood_axe",      "rock_large", 5);
        HarvestConfig.RegisterToolDamage("wood_axe",      "rock_small", 5);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "tree_pine",  8);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "tree_dead",  8);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "tree_palm",  8);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "bush",       8);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "tree_pine",  10);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "tree_dead",  10);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "tree_palm",  10);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "bush",       10);

        // Drop tables
        HarvestConfig.RegisterDrop("rock_large", "stone", 3);
        HarvestConfig.RegisterDrop("rock_small", "stone", 1);
        HarvestConfig.RegisterDrop("tree_pine",  "wood",  4);
        HarvestConfig.RegisterDrop("tree_dead",  "wood",  2);
        HarvestConfig.RegisterDrop("tree_palm",  "wood",  1);
        HarvestConfig.RegisterDrop("bush",       "wood",  1);
    }

    // ---- lifecycle forwarded from IMod hooks --------------------------------

    private static void BaseModHandleClientConnected(ReducerContext ctx, Identity identity)
    {
        var existing = ctx.Db.Player.Identity.Find(identity);

        if (existing is not null)
        {
            var player = existing.Value;
            player.IsOnline = true;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"Player '{player.Name}' reconnected.");
        }
        else
        {
            ctx.Db.Player.Insert(new Player
            {
                Identity    = identity,
                Name        = $"Player_{identity.ToString()[..8]}",
                PosX        = 0f,
                PosY        = 0.3f,
                PosZ        = 1f,
                RotY        = (float)Math.PI,
                Health      = 100f,
                MaxHealth   = 100f,
                Stamina     = 100f,
                MaxStamina  = 100f,
                IsOnline    = true,
                ColorHex    = "#3CB4E5",
            });
            ctx.Db.InventoryItem.Insert(new InventoryItem { OwnerId = identity, ItemType = "wood_pickaxe", Quantity = 1, Slot = 0 });
            ctx.Db.InventoryItem.Insert(new InventoryItem { OwnerId = identity, ItemType = "wood_axe",     Quantity = 1, Slot = 1 });
            Log.Info($"[BaseMod] New player created: {identity}");
        }
    }

    private static void BaseModHandleClientDisconnected(ReducerContext ctx, Identity identity)
    {
        var existing = ctx.Db.Player.Identity.Find(identity);
        if (existing is not null)
        {
            var player = existing.Value;
            player.IsOnline = false;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"[BaseMod] Player '{player.Name}' disconnected.");
        }
    }
}
