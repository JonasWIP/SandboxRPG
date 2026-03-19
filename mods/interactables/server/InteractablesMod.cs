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
            SeedDemoStructures(ctx);
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

    /// <summary>Inserts a public access control entry for a placed structure.</summary>
    private static void InsertAccessControl(ReducerContext ctx, PlacedStructure s)
    {
        ctx.Db.AccessControl.Insert(new AccessControl
        {
            EntityId = s.Id,
            EntityTable = EntityTables.PlacedStructure,
            OwnerId = s.OwnerId,
            IsPublic = true,
        });
    }

    /// <summary>Removes the access control entry for a placed structure.</summary>
    private static void RemoveAccessControl(ReducerContext ctx, PlacedStructure s)
    {
        var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
        if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
    }

    /// <summary>Drops all container slot contents as world items and deletes the slots.</summary>
    private static void DropContainerContents(ReducerContext ctx, PlacedStructure s)
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
    }

    private static void RegisterStructureHooks()
    {
        // CHEST: access control on place, drop contents + cleanup on remove
        StructureHooks.RegisterOnPlace("chest", InsertAccessControl);
        StructureHooks.RegisterOnRemove("chest", (ctx, s) =>
        {
            DropContainerContents(ctx, s);
            RemoveAccessControl(ctx, s);
        });

        // FURNACE: access control + furnace state cleanup
        StructureHooks.RegisterOnPlace("furnace", InsertAccessControl);
        StructureHooks.RegisterOnRemove("furnace", (ctx, s) =>
        {
            DropContainerContents(ctx, s);
            var fs = ctx.Db.FurnaceState.StructureId.Find(s.Id);
            if (fs is not null) ctx.Db.FurnaceState.Delete(fs.Value);
            RemoveAccessControl(ctx, s);
        });

        // CRAFTING TABLE: just access control
        StructureHooks.RegisterOnPlace("crafting_table", InsertAccessControl);
        StructureHooks.RegisterOnRemove("crafting_table", (ctx, s) => RemoveAccessControl(ctx, s));

        // SIGN: access control + sign text
        StructureHooks.RegisterOnPlace("sign", (ctx, s) =>
        {
            ctx.Db.SignText.Insert(new SignText { StructureId = s.Id, Text = "" });
            InsertAccessControl(ctx, s);
        });
        StructureHooks.RegisterOnRemove("sign", (ctx, s) =>
        {
            var st = ctx.Db.SignText.StructureId.Find(s.Id);
            if (st is not null) ctx.Db.SignText.Delete(st.Value);
            RemoveAccessControl(ctx, s);
        });
    }

    /// <summary>Places demo interactable structures near spawn for testing.</summary>
    private static void SeedDemoStructures(ReducerContext ctx)
    {
        var owner = ctx.Sender;
        void Place(string type, float x, float z)
        {
            float maxHp = StructureConfig.GetMaxHealth(type);
            var s = ctx.Db.PlacedStructure.Insert(new PlacedStructure
            {
                OwnerId = owner,
                StructureType = type,
                PosX = x, PosY = 0f, PosZ = z, RotY = 0f,
                Health = maxHp, MaxHealth = maxHp,
            });
            StructureHooks.FireOnPlace(ctx, s);
        }

        Place("chest",          3f, -3f);
        Place("furnace",        5f, -3f);
        Place("crafting_table", 7f, -3f);
        Place("sign",           9f, -3f);
        Log.Info("[InteractablesMod] Demo structures placed near spawn.");
    }

    private static void SeedCraftingTableRecipes(ReducerContext ctx)
    {
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "furnace", ResultQuantity = 1,
            Ingredients = "stone:8,iron:2", CraftTimeSeconds = 5f, Station = "crafting_table",
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
