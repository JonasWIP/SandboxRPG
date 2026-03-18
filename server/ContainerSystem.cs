// server/ContainerSystem.cs
using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "container_slot", Public = true)]
    public partial struct ContainerSlot
    {
        [AutoInc][PrimaryKey] public ulong Id;
        public ulong ContainerId;
        public string ContainerTable;
        public int Slot;
        public string ItemType;
        public uint Quantity;
    }

    [Reducer]
    public static void ContainerDeposit(ReducerContext ctx, ulong containerId, string containerTable, ulong inventoryItemId, int toSlot, uint quantity)
    {
        if (!AccessControlHelper.CanAccess(ctx, containerId, containerTable))
            throw new Exception("Access denied.");

        var invItem = ctx.Db.InventoryItem.Id.Find(inventoryItemId);
        if (invItem is null) throw new Exception("Inventory item not found.");
        var src = invItem.Value;
        if (src.OwnerId != ctx.Sender) throw new Exception("Not your item.");
        if (quantity == 0 || quantity > src.Quantity) throw new Exception("Invalid quantity.");

        // Look up structure type to get slot count
        string structureType = "";
        if (containerTable == EntityTables.PlacedStructure)
        {
            var ps = ctx.Db.PlacedStructure.Id.Find(containerId);
            if (ps is not null) structureType = ps.Value.StructureType;
        }
        int slotCount = ContainerConfig.GetSlotCount(structureType);
        if (slotCount == 0) throw new Exception("Not a container.");
        if (toSlot < 0 || toSlot >= slotCount) throw new Exception("Invalid slot.");

        // Find existing container slot content
        ContainerSlot? existing = FindContainerSlot(ctx, containerId, containerTable, toSlot);

        if (existing is not null)
        {
            var ex = existing.Value;
            if (!string.IsNullOrEmpty(ex.ItemType) && ex.ItemType != src.ItemType)
                throw new Exception("Slot occupied by different item type.");
            ex.ItemType = src.ItemType;
            ex.Quantity += quantity;
            ctx.Db.ContainerSlot.Id.Update(ex);
        }
        else
        {
            ctx.Db.ContainerSlot.Insert(new ContainerSlot
            {
                ContainerId = containerId,
                ContainerTable = containerTable,
                Slot = toSlot,
                ItemType = src.ItemType,
                Quantity = quantity,
            });
        }

        if (quantity >= src.Quantity)
            ctx.Db.InventoryItem.Delete(src);
        else
        {
            src.Quantity -= quantity;
            ctx.Db.InventoryItem.Id.Update(src);
        }
    }

    [Reducer]
    public static void ContainerWithdraw(ReducerContext ctx, ulong containerId, string containerTable, int fromSlot, uint quantity)
    {
        if (!AccessControlHelper.CanAccess(ctx, containerId, containerTable))
            throw new Exception("Access denied.");

        var found = FindContainerSlot(ctx, containerId, containerTable, fromSlot);
        if (found is null) throw new Exception("Container slot is empty.");
        var slot = found.Value;
        if (quantity == 0 || quantity > slot.Quantity) throw new Exception("Invalid quantity.");

        AddOrStackInventoryItem(ctx, ctx.Sender, slot.ItemType, quantity,
            FindOpenHotbarSlot(ctx, ctx.Sender));

        if (quantity >= slot.Quantity)
            ctx.Db.ContainerSlot.Delete(slot);
        else
        {
            slot.Quantity -= quantity;
            ctx.Db.ContainerSlot.Id.Update(slot);
        }
    }

    [Reducer]
    public static void ContainerTransfer(ReducerContext ctx, ulong containerId, string containerTable, int fromSlot, int toSlot)
    {
        if (!AccessControlHelper.CanAccess(ctx, containerId, containerTable))
            throw new Exception("Access denied.");

        ContainerSlot? srcSlot = null, dstSlot = null;
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
        {
            if (cs.ContainerId != containerId || cs.ContainerTable != containerTable) continue;
            if (cs.Slot == fromSlot) srcSlot = cs;
            if (cs.Slot == toSlot) dstSlot = cs;
        }

        if (srcSlot is null) throw new Exception("Source slot is empty.");
        var src = srcSlot.Value;

        if (dstSlot is null)
        {
            src.Slot = toSlot;
            ctx.Db.ContainerSlot.Id.Update(src);
        }
        else
        {
            var dst = dstSlot.Value;
            if (dst.ItemType == src.ItemType)
            {
                dst.Quantity += src.Quantity;
                ctx.Db.ContainerSlot.Id.Update(dst);
                ctx.Db.ContainerSlot.Delete(src);
            }
            else
            {
                int tempSlot = src.Slot;
                src.Slot = dst.Slot;
                dst.Slot = tempSlot;
                ctx.Db.ContainerSlot.Id.Update(src);
                ctx.Db.ContainerSlot.Id.Update(dst);
            }
        }
    }
}
