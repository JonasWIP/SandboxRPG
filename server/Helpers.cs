// server/Helpers.cs
using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // TIMESTAMP HELPERS
    // =========================================================================

    /// <summary>Current reducer timestamp in milliseconds (UTC).</summary>
    internal static ulong NowMs(ReducerContext ctx) =>
        (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds();

    /// <summary>Current reducer timestamp in microseconds (UTC).</summary>
    internal static ulong NowUs(ReducerContext ctx) =>
        NowMs(ctx) * 1000;

    // =========================================================================
    // CONTAINER SLOT HELPERS
    // =========================================================================

    /// <summary>Finds a container slot by container ID, table, and slot index.</summary>
    internal static ContainerSlot? FindContainerSlot(
        ReducerContext ctx, ulong containerId, string containerTable, int slot)
    {
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
            if (cs.ContainerId == containerId && cs.ContainerTable == containerTable && cs.Slot == slot)
                return cs;
        return null;
    }

    // =========================================================================
    // INVENTORY HELPERS
    // =========================================================================

    /// <summary>
    /// Adds items to a player's inventory, stacking onto an existing slot if
    /// one matches the item type, otherwise inserting a new row.
    /// </summary>
    internal static void AddOrStackInventoryItem(
        ReducerContext ctx, Identity owner, string itemType, uint quantity, int slot = GameConstants.BagSlot)
    {
        foreach (var inv in ctx.Db.InventoryItem.Iter())
        {
            if (inv.OwnerId == owner && inv.ItemType == itemType)
            {
                var updated = inv;
                updated.Quantity += quantity;
                ctx.Db.InventoryItem.Id.Update(updated);
                return;
            }
        }
        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId = owner,
            ItemType = itemType,
            Quantity = quantity,
            Slot = slot,
        });
    }

    /// <summary>
    /// Consumes a quantity of an item type from a player's inventory across
    /// multiple stacks. Returns true if successful.
    /// Caller must verify availability first (use CountInventoryItem).
    /// </summary>
    internal static bool ConsumeFromInventory(
        ReducerContext ctx, Identity owner, string itemType, uint quantity)
    {
        uint remaining = quantity;
        var toDelete = new List<InventoryItem>();
        var toUpdate = new List<(InventoryItem item, uint newQty)>();

        foreach (var inv in ctx.Db.InventoryItem.Iter())
        {
            if (remaining == 0) break;
            if (inv.OwnerId != owner || inv.ItemType != itemType) continue;

            if (inv.Quantity <= remaining)
            {
                remaining -= inv.Quantity;
                toDelete.Add(inv);
            }
            else
            {
                toUpdate.Add((inv, inv.Quantity - remaining));
                remaining = 0;
            }
        }

        if (remaining > 0) return false;

        foreach (var item in toDelete) ctx.Db.InventoryItem.Delete(item);
        foreach (var (item, newQty) in toUpdate)
        {
            var updated = item;
            updated.Quantity = newQty;
            ctx.Db.InventoryItem.Id.Update(updated);
        }
        return true;
    }

    /// <summary>Counts how many of an item type a player has across all stacks.</summary>
    internal static uint CountInventoryItem(ReducerContext ctx, Identity owner, string itemType)
    {
        uint total = 0;
        foreach (var inv in ctx.Db.InventoryItem.Iter())
            if (inv.OwnerId == owner && inv.ItemType == itemType)
                total += inv.Quantity;
        return total;
    }
}
