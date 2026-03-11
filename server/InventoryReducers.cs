using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // INVENTORY REDUCERS
    // =========================================================================

    [Reducer]
    public static void PickupItem(ReducerContext ctx, ulong worldItemId)
    {
        var worldItem = ctx.Db.WorldItem.Id.Find(worldItemId);
        if (worldItem is null) return;

        var item = worldItem.Value;
        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (player is null) return;

        // Distance check — 5 unit pickup radius
        float dx = item.PosX - player.Value.PosX;
        float dz = item.PosZ - player.Value.PosZ;
        if (dx * dx + dz * dz > 25f)
            throw new Exception("Too far away to pick up.");

        // Stack with existing inventory slot if possible
        bool stacked = false;
        foreach (var invItem in ctx.Db.InventoryItem.Iter())
        {
            if (invItem.OwnerId == ctx.Sender && invItem.ItemType == item.ItemType)
            {
                var updated = invItem;
                updated.Quantity += item.Quantity;
                ctx.Db.InventoryItem.Id.Update(updated);
                stacked = true;
                break;
            }
        }

        if (!stacked)
        {
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender,
                ItemType = item.ItemType,
                Quantity = item.Quantity,
                Slot = -1,
            });
        }

        ctx.Db.WorldItem.Delete(item);
    }

    [Reducer]
    public static void DropItem(ReducerContext ctx, ulong inventoryItemId, uint quantity)
    {
        var invItem = ctx.Db.InventoryItem.Id.Find(inventoryItemId);
        if (invItem is null) return;

        var item = invItem.Value;
        if (item.OwnerId != ctx.Sender) return;
        if (quantity == 0 || quantity > item.Quantity) return;

        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (player is null) return;

        // Spawn near player feet
        ctx.Db.WorldItem.Insert(new WorldItem
        {
            ItemType = item.ItemType,
            Quantity = quantity,
            PosX = player.Value.PosX + 1f,
            PosY = player.Value.PosY,
            PosZ = player.Value.PosZ + 1f,
        });

        if (quantity >= item.Quantity)
            ctx.Db.InventoryItem.Delete(item);
        else
        {
            item.Quantity -= quantity;
            ctx.Db.InventoryItem.Id.Update(item);
        }
    }

    [Reducer]
    public static void MoveItemToSlot(ReducerContext ctx, ulong inventoryItemId, int slot)
    {
        if (slot < -1 || slot > 8) return;

        var invItem = ctx.Db.InventoryItem.Id.Find(inventoryItemId);
        if (invItem is null) return;

        var item = invItem.Value;
        if (item.OwnerId != ctx.Sender) return;

        item.Slot = slot;
        ctx.Db.InventoryItem.Id.Update(item);
    }

    // =========================================================================
    // SHARED HELPERS
    // =========================================================================

    /// <summary>Parses "wood:4,stone:2" ingredient strings into typed tuples.</summary>
    internal static List<(string itemType, uint quantity)> ParseIngredients(string ingredients)
    {
        var result = new List<(string, uint)>();
        if (string.IsNullOrEmpty(ingredients)) return result;

        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2 && uint.TryParse(kv[1], out uint qty))
                result.Add((kv[0].Trim(), qty));
        }
        return result;
    }
}
