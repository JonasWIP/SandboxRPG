// mods/interactables/server/InteractablesMod.cs
using SpacetimeDB;
using System;
using System.Collections.Generic;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly InteractablesModImpl _interactablesMod = new();

    private sealed class InteractablesModImpl : IMod
    {
        public InteractablesModImpl() => ModLoader.Register(this);

        public string   Name         => "interactables";
        public string   Version      => "1.0.0";
        public string[] Dependencies => new[] { "base" };

        public void Seed(ReducerContext ctx)
        {
            RegisterContainerTypes();
            RegisterSmeltRecipes();
            RegisterStructureHooks();
            SeedCraftingTableRecipes(ctx);
            Log.Info("[InteractablesMod] Seeded.");
        }
    }

    private static void RegisterContainerTypes()
    {
        ContainerConfig.Register("chest",   16);
        ContainerConfig.Register("furnace",  2);
    }

    private static void RegisterSmeltRecipes()
    {
        SmeltConfig.Register("raw_iron", "iron", 1, 10_000);
    }

    private static void RegisterStructureHooks()
    {
        // CHEST: access control on place, drop contents + cleanup on remove
        StructureHooks.RegisterOnPlace("chest", (ctx, s) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = s.OwnerId,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("chest", (ctx, s) =>
        {
            var toDelete = new List<ContainerSlot>();
            foreach (var cs in ctx.Db.ContainerSlot.Iter())
                if (cs.ContainerId == s.Id && cs.ContainerTable == EntityTables.PlacedStructure)
                    toDelete.Add(cs);
            foreach (var cs in toDelete)
            {
                if (!string.IsNullOrEmpty(cs.ItemType) && cs.Quantity > 0)
                {
                    ctx.Db.WorldItem.Insert(new WorldItem
                    {
                        ItemType = cs.ItemType, Quantity = cs.Quantity,
                        PosX = s.PosX, PosY = s.PosY, PosZ = s.PosZ,
                    });
                }
                ctx.Db.ContainerSlot.Delete(cs);
            }
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // FURNACE: access control + furnace state cleanup
        StructureHooks.RegisterOnPlace("furnace", (ctx, s) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = s.OwnerId,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("furnace", (ctx, s) =>
        {
            var toDelete = new List<ContainerSlot>();
            foreach (var cs in ctx.Db.ContainerSlot.Iter())
                if (cs.ContainerId == s.Id && cs.ContainerTable == EntityTables.PlacedStructure)
                    toDelete.Add(cs);
            foreach (var cs in toDelete)
            {
                if (!string.IsNullOrEmpty(cs.ItemType) && cs.Quantity > 0)
                {
                    ctx.Db.WorldItem.Insert(new WorldItem
                    {
                        ItemType = cs.ItemType, Quantity = cs.Quantity,
                        PosX = s.PosX, PosY = s.PosY, PosZ = s.PosZ,
                    });
                }
                ctx.Db.ContainerSlot.Delete(cs);
            }
            var fs = ctx.Db.FurnaceState.StructureId.Find(s.Id);
            if (fs is not null) ctx.Db.FurnaceState.Delete(fs.Value);
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // CRAFTING TABLE: just access control
        StructureHooks.RegisterOnPlace("crafting_table", (ctx, s) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = s.OwnerId,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("crafting_table", (ctx, s) =>
        {
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // SIGN: access control + sign text
        StructureHooks.RegisterOnPlace("sign", (ctx, s) =>
        {
            ctx.Db.SignText.Insert(new SignText { StructureId = s.Id, Text = "" });
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = s.OwnerId,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("sign", (ctx, s) =>
        {
            var st = ctx.Db.SignText.StructureId.Find(s.Id);
            if (st is not null) ctx.Db.SignText.Delete(st.Value);
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });
    }

    private static void SeedCraftingTableRecipes(ReducerContext ctx)
    {
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "furnace", ResultQuantity = 1,
            Ingredients = "stone:8,iron:2", CraftTimeSeconds = 5f, Station = "",
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "crafting_table", ResultQuantity = 1,
            Ingredients = "wood:10,iron:2", CraftTimeSeconds = 6f, Station = "",
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "sign", ResultQuantity = 1,
            Ingredients = "wood:2", CraftTimeSeconds = 1f, Station = "",
        });
    }
}
