// mods/interactables/server/InteractablesReducers.cs
using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // FURNACE REDUCERS
    // =========================================================================

    [Reducer]
    public static void FurnaceStartSmelt(ReducerContext ctx, ulong structureId, string inputItem)
    {
        // Verify the structure exists and is a furnace
        var structure = ctx.Db.PlacedStructure.Id.Find(structureId);
        if (structure is null)
            throw new Exception("Furnace not found.");
        if (structure.Value.StructureType != "furnace")
            throw new Exception("Structure is not a furnace.");

        // Check access
        var ac = ctx.Db.AccessControl.StructureId.Find(structureId);
        if (ac is null || (!ac.Value.IsPublic && ac.Value.OwnerId != ctx.Sender))
            throw new Exception("You do not have access to this furnace.");

        // Ensure no active smelt
        var existing = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (existing is not null)
            throw new Exception("Furnace is already smelting.");

        // Validate recipe
        var recipe = SmeltConfig.Get(inputItem);
        if (recipe is null)
            throw new Exception($"No smelt recipe for '{inputItem}'.");

        // Consume input item from the furnace's container slot
        ContainerSlot? inputSlot = null;
        foreach (var slot in ctx.Db.ContainerSlot.Iter())
        {
            if (slot.ContainerId == structureId && slot.ItemType == inputItem)
            {
                inputSlot = slot;
                break;
            }
        }
        if (inputSlot is null)
            throw new Exception($"Furnace does not contain '{inputItem}'.");

        var s = inputSlot.Value;
        if (s.Quantity <= 1)
            ctx.Db.ContainerSlot.Delete(s);
        else
        {
            s.Quantity -= 1;
            ctx.Db.ContainerSlot.Id.Update(s);
        }

        ulong nowMs = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds();

        ctx.Db.FurnaceState.Insert(new FurnaceState
        {
            StructureId = structureId,
            RecipeType  = inputItem,
            StartTimeMs = nowMs,
            DurationMs  = recipe.Value.DurationMs,
            Complete    = false,
        });

        Log.Info($"[Interactables] Furnace {structureId} started smelting '{inputItem}'.");
    }

    [Reducer]
    public static void FurnaceCollect(ReducerContext ctx, ulong structureId)
    {
        // Verify the structure exists and is a furnace
        var structure = ctx.Db.PlacedStructure.Id.Find(structureId);
        if (structure is null)
            throw new Exception("Furnace not found.");

        // Check access
        var ac = ctx.Db.AccessControl.StructureId.Find(structureId);
        if (ac is null || (!ac.Value.IsPublic && ac.Value.OwnerId != ctx.Sender))
            throw new Exception("You do not have access to this furnace.");

        var state = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (state is null)
            throw new Exception("Furnace is not smelting.");

        var fs = state.Value;
        ulong nowMs = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds();

        if (!fs.Complete && nowMs < fs.StartTimeMs + fs.DurationMs)
            throw new Exception("Smelting is not finished yet.");

        var recipe = SmeltConfig.Get(fs.RecipeType);
        if (recipe is null)
            throw new Exception($"No smelt recipe for '{fs.RecipeType}'.");

        // Give output to player
        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId  = ctx.Sender,
            ItemType = recipe.Value.OutputItem,
            Quantity = recipe.Value.OutputQuantity,
            Slot     = -1,
        });

        ctx.Db.FurnaceState.Delete(fs);

        Log.Info($"[Interactables] Player collected '{recipe.Value.OutputItem}' from furnace {structureId}.");
    }

    [Reducer]
    public static void FurnaceCancelSmelt(ReducerContext ctx, ulong structureId)
    {
        // Verify structure exists
        var structure = ctx.Db.PlacedStructure.Id.Find(structureId);
        if (structure is null)
            throw new Exception("Furnace not found.");

        // Check access — only owner can cancel
        var ac = ctx.Db.AccessControl.StructureId.Find(structureId);
        if (ac is null || ac.Value.OwnerId != ctx.Sender)
            throw new Exception("You do not have permission to cancel smelting.");

        var state = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (state is null)
            throw new Exception("Furnace is not smelting.");

        var fs = state.Value;

        // Refund input item back to the furnace container
        ctx.Db.ContainerSlot.Insert(new ContainerSlot
        {
            ContainerId   = structureId,
            ContainerType = "furnace",
            ItemType      = fs.RecipeType,
            Quantity      = 1,
            Slot          = 0,
        });

        ctx.Db.FurnaceState.Delete(fs);

        Log.Info($"[Interactables] Smelting cancelled in furnace {structureId}.");
    }

    // =========================================================================
    // SIGN REDUCER
    // =========================================================================

    [Reducer]
    public static void UpdateSignText(ReducerContext ctx, ulong structureId, string text)
    {
        // Verify the structure exists and is a sign
        var structure = ctx.Db.PlacedStructure.Id.Find(structureId);
        if (structure is null)
            throw new Exception("Sign not found.");
        if (structure.Value.StructureType != "sign")
            throw new Exception("Structure is not a sign.");

        // Check access — only owner can update text
        var ac = ctx.Db.AccessControl.StructureId.Find(structureId);
        if (ac is null || ac.Value.OwnerId != ctx.Sender)
            throw new Exception("You do not have permission to edit this sign.");

        var existingText = ctx.Db.SignText.StructureId.Find(structureId);
        if (existingText is not null)
        {
            var st = existingText.Value;
            st.Text = text;
            ctx.Db.SignText.StructureId.Update(st);
        }
        else
        {
            ctx.Db.SignText.Insert(new SignText
            {
                StructureId = structureId,
                Text        = text,
            });
        }

        Log.Info($"[Interactables] Sign {structureId} updated by {ctx.Sender}.");
    }
}
