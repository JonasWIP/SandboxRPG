using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // BUILDING REDUCERS
    // =========================================================================

    [Reducer]
    public static void PlaceStructure(ReducerContext ctx, string structureType, float posX, float posY, float posZ, float rotY)
    {
        var identity = ctx.Sender;

        // Require the item in inventory
        InventoryItem? foundItem = null;
        foreach (var inv in ctx.Db.InventoryItem.Iter())
        {
            if (inv.OwnerId == identity && inv.ItemType == structureType)
            {
                foundItem = inv;
                break;
            }
        }
        if (foundItem is null)
            throw new Exception($"You don't have a {structureType} to place.");

        float maxHealth = structureType switch
        {
            "wood_wall"   => 100f,
            "stone_wall"  => 250f,
            "wood_floor"  => 80f,
            "stone_floor" => 200f,
            "wood_door"   => 60f,
            "campfire"    => 50f,
            "workbench"   => 100f,
            "chest"       => 80f,
            _             => 100f,
        };

        ctx.Db.PlacedStructure.Insert(new PlacedStructure
        {
            OwnerId = identity,
            StructureType = structureType,
            PosX = posX,
            PosY = posY,
            PosZ = posZ,
            RotY = rotY,
            Health = maxHealth,
            MaxHealth = maxHealth,
        });

        // Consume one item
        var item = foundItem.Value;
        if (item.Quantity <= 1)
            ctx.Db.InventoryItem.Delete(item);
        else
        {
            item.Quantity -= 1;
            ctx.Db.InventoryItem.Id.Update(item);
        }

        Log.Info($"Player placed {structureType} at ({posX:F1}, {posY:F1}, {posZ:F1})");
    }

    [Reducer]
    public static void RemoveStructure(ReducerContext ctx, ulong structureId)
    {
        var structure = ctx.Db.PlacedStructure.Id.Find(structureId);
        if (structure is null) return;

        var s = structure.Value;
        if (s.OwnerId != ctx.Sender)
            throw new Exception("You can only remove your own structures.");

        // Refund the item
        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId = ctx.Sender,
            ItemType = s.StructureType,
            Quantity = 1,
            Slot = -1,
        });

        ctx.Db.PlacedStructure.Delete(s);
    }
}
