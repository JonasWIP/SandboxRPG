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

    // ---- registration helpers -----------------------------------------------

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
        // ── CHEST ────────────────────────────────────────────────────────────
        StructureHooks.RegisterOnPlace("chest", (ctx, structure) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                StructureId = structure.Id,
                OwnerId     = structure.OwnerId,
                IsPublic    = true,
            });
        });

        StructureHooks.RegisterOnRemove("chest", (ctx, structure) =>
        {
            // Collect slots first to avoid delete-during-iteration
            var slotsToDelete = new List<ContainerSlot>();
            foreach (var slot in ctx.Db.ContainerSlot.Iter())
            {
                if (slot.ContainerId == structure.Id)
                    slotsToDelete.Add(slot);
            }

            // Drop contents as world items
            foreach (var slot in slotsToDelete)
            {
                ctx.Db.WorldItem.Insert(new WorldItem
                {
                    ItemType = slot.ItemType,
                    Quantity = slot.Quantity,
                    PosX     = structure.PosX,
                    PosY     = structure.PosY + 0.5f,
                    PosZ     = structure.PosZ,
                });
                ctx.Db.ContainerSlot.Delete(slot);
            }

            // Remove access control
            var ac = ctx.Db.AccessControl.StructureId.Find(structure.Id);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // ── FURNACE ──────────────────────────────────────────────────────────
        StructureHooks.RegisterOnPlace("furnace", (ctx, structure) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                StructureId = structure.Id,
                OwnerId     = structure.OwnerId,
                IsPublic    = true,
            });
        });

        StructureHooks.RegisterOnRemove("furnace", (ctx, structure) =>
        {
            // Collect container slots first
            var slotsToDelete = new List<ContainerSlot>();
            foreach (var slot in ctx.Db.ContainerSlot.Iter())
            {
                if (slot.ContainerId == structure.Id)
                    slotsToDelete.Add(slot);
            }

            // Drop contents as world items
            foreach (var slot in slotsToDelete)
            {
                ctx.Db.WorldItem.Insert(new WorldItem
                {
                    ItemType = slot.ItemType,
                    Quantity = slot.Quantity,
                    PosX     = structure.PosX,
                    PosY     = structure.PosY + 0.5f,
                    PosZ     = structure.PosZ,
                });
                ctx.Db.ContainerSlot.Delete(slot);
            }

            // Clean up furnace state
            var fs = ctx.Db.FurnaceState.StructureId.Find(structure.Id);
            if (fs is not null) ctx.Db.FurnaceState.Delete(fs.Value);

            // Remove access control
            var ac = ctx.Db.AccessControl.StructureId.Find(structure.Id);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // ── CRAFTING TABLE ───────────────────────────────────────────────────
        StructureHooks.RegisterOnPlace("crafting_table", (ctx, structure) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                StructureId = structure.Id,
                OwnerId     = structure.OwnerId,
                IsPublic    = true,
            });
        });

        StructureHooks.RegisterOnRemove("crafting_table", (ctx, structure) =>
        {
            var ac = ctx.Db.AccessControl.StructureId.Find(structure.Id);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // ── SIGN ─────────────────────────────────────────────────────────────
        StructureHooks.RegisterOnPlace("sign", (ctx, structure) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                StructureId = structure.Id,
                OwnerId     = structure.OwnerId,
                IsPublic    = false,
            });
            ctx.Db.SignText.Insert(new SignText
            {
                StructureId = structure.Id,
                Text        = "",
            });
        });

        StructureHooks.RegisterOnRemove("sign", (ctx, structure) =>
        {
            var st = ctx.Db.SignText.StructureId.Find(structure.Id);
            if (st is not null) ctx.Db.SignText.Delete(st.Value);

            var ac = ctx.Db.AccessControl.StructureId.Find(structure.Id);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });
    }

    private static void SeedCraftingTableRecipes(ReducerContext ctx)
    {
        // Add crafting-table-specific recipes not already provided by BaseMod.
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType   = "furnace",
            ResultQuantity   = 1,
            Ingredients      = "stone:8,iron:2",
            CraftTimeSeconds = 5f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType   = "crafting_table",
            ResultQuantity   = 1,
            Ingredients      = "wood:10,iron:2",
            CraftTimeSeconds = 6f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType   = "sign",
            ResultQuantity   = 1,
            Ingredients      = "wood:2",
            CraftTimeSeconds = 1f,
        });
        Log.Info("[InteractablesMod] Seeded crafting table recipes.");
    }
}
