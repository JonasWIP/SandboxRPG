// mods/interactables/server/InteractablesReducers.cs
using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void FurnaceStartSmelt(ReducerContext ctx, ulong structureId)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        var existing = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (existing is not null)
            throw new Exception("Furnace is already smelting.");

        // Find input slot content
        var inputSlot = FindContainerSlot(ctx, structureId, EntityTables.PlacedStructure, GameConstants.FurnaceInputSlot);

        if (inputSlot is null || string.IsNullOrEmpty(inputSlot.Value.ItemType))
            throw new Exception("Furnace input is empty.");

        var input = inputSlot.Value;
        var recipe = SmeltConfig.Get(input.ItemType);
        if (recipe is null)
            throw new Exception($"Cannot smelt {input.ItemType}.");

        var r = recipe.Value;
        var now = NowMs(ctx);

        ctx.Db.FurnaceState.Insert(new FurnaceState
        {
            StructureId = structureId,
            RecipeType = input.ItemType,
            StartTimeMs = now,
            DurationMs = r.DurationMs,
            Complete = false,
        });

        // Consume one input item
        if (input.Quantity <= 1)
            ctx.Db.ContainerSlot.Delete(input);
        else
        {
            input.Quantity -= 1;
            ctx.Db.ContainerSlot.Id.Update(input);
        }

        Log.Info($"Furnace {structureId} started smelting {r.InputItem}");
    }

    [Reducer]
    public static void FurnaceCollect(ReducerContext ctx, ulong structureId)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        var state = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (state is null) throw new Exception("Furnace is not smelting.");

        var fs = state.Value;
        var now = NowMs(ctx);

        if (now < fs.StartTimeMs + fs.DurationMs)
            throw new Exception("Smelting not complete yet.");

        var recipe = SmeltConfig.Get(fs.RecipeType);
        if (recipe is null) throw new Exception("Smelt recipe not found.");

        var r = recipe.Value;

        // Place result in output slot or add to it
        var outputSlot = FindContainerSlot(ctx, structureId, EntityTables.PlacedStructure, GameConstants.FurnaceOutputSlot);

        if (outputSlot is not null)
        {
            var os = outputSlot.Value;
            if (!string.IsNullOrEmpty(os.ItemType) && os.ItemType != r.OutputItem)
                throw new Exception("Output slot occupied by different item. Withdraw first.");
            os.ItemType = r.OutputItem;
            os.Quantity += r.OutputQuantity;
            ctx.Db.ContainerSlot.Id.Update(os);
        }
        else
        {
            ctx.Db.ContainerSlot.Insert(new ContainerSlot
            {
                ContainerId = structureId,
                ContainerTable = EntityTables.PlacedStructure,
                Slot = GameConstants.FurnaceOutputSlot,
                ItemType = r.OutputItem,
                Quantity = r.OutputQuantity,
            });
        }

        ctx.Db.FurnaceState.Delete(fs);
        Log.Info($"Furnace {structureId} produced {r.OutputQuantity}x {r.OutputItem}");
    }

    [Reducer]
    public static void FurnaceCancelSmelt(ReducerContext ctx, ulong structureId)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        var state = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (state is null) throw new Exception("Furnace is not smelting.");

        var fs = state.Value;

        // Return input item to input slot
        var inputSlot = FindContainerSlot(ctx, structureId, EntityTables.PlacedStructure, GameConstants.FurnaceInputSlot);

        if (inputSlot is not null)
        {
            var slot = inputSlot.Value;
            slot.ItemType = fs.RecipeType;
            slot.Quantity += 1;
            ctx.Db.ContainerSlot.Id.Update(slot);
        }
        else
        {
            ctx.Db.ContainerSlot.Insert(new ContainerSlot
            {
                ContainerId = structureId,
                ContainerTable = EntityTables.PlacedStructure,
                Slot = GameConstants.FurnaceInputSlot,
                ItemType = fs.RecipeType,
                Quantity = 1,
            });
        }

        ctx.Db.FurnaceState.Delete(fs);
        Log.Info($"Furnace {structureId} smelt cancelled");
    }

    [Reducer]
    public static void UpdateSignText(ReducerContext ctx, ulong structureId, string text)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        // Public signs can be edited by anyone; private signs require ownership
        var ac = AccessControlHelper.Find(ctx, structureId, EntityTables.PlacedStructure);
        if (ac is not null && !ac.Value.IsPublic && ac.Value.OwnerId != ctx.Sender)
            throw new Exception("Only the owner can edit a private sign.");

        if (text.Length > GameConstants.MaxSignTextLength) text = text[..GameConstants.MaxSignTextLength];

        var existing = ctx.Db.SignText.StructureId.Find(structureId);
        if (existing is not null)
        {
            var row = existing.Value;
            row.Text = text;
            ctx.Db.SignText.StructureId.Update(row);
        }
        else
        {
            ctx.Db.SignText.Insert(new SignText
            {
                StructureId = structureId,
                Text = text,
            });
        }
    }
}
