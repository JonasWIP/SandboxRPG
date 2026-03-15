using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void HarvestWorldObject(ReducerContext ctx, ulong id, string toolType)
    {
        var obj = ctx.Db.WorldObject.Id.Find(id);
        if (obj is null) return;
        var o = obj.Value;

        // Validate the player owns the stated tool (empty string = bare hands, always valid)
        if (!string.IsNullOrEmpty(toolType))
        {
            bool found = false;
            foreach (var item in ctx.Db.InventoryItem.Iter())
            {
                if (item.OwnerId == ctx.Sender && item.ItemType == toolType)
                { found = true; break; }
            }
            if (!found) return;
        }

        uint damage    = ToolDamage(toolType, o.ObjectType);
        uint newHealth = o.Health <= damage ? 0 : o.Health - damage;

        ctx.Db.WorldObject.Delete(o);

        if (newHealth == 0)
        {
            ctx.Db.WorldItem.Insert(new WorldItem
            {
                ItemType = DropTypeFor(o.ObjectType),
                Quantity = DropQuantityFor(o.ObjectType),
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
            });
        }
        else
        {
            ctx.Db.WorldObject.Insert(new WorldObject
            {
                ObjectType = o.ObjectType,
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
                RotY = o.RotY, Health = newHealth, MaxHealth = o.MaxHealth,
            });
        }
    }

    private static uint ToolDamage(string toolType, string objectType)
    {
        bool isTree = objectType is "tree_pine" or "tree_dead" or "tree_palm" or "bush";
        bool isRock = objectType is "rock_large" or "rock_small";
        return toolType switch
        {
            "wood_axe"      => isTree ? 34u : 5u,
            "wood_pickaxe"  => isRock ? 34u : 5u,
            "stone_pickaxe" => isRock ? 50u : 8u,
            "iron_pickaxe"  => isRock ? 75u : 10u,
            _               => 5u,
        };
    }

    private static string DropTypeFor(string objectType) => objectType switch
    {
        "rock_large" or "rock_small" => "stone",
        _ => "wood",
    };

    private static uint DropQuantityFor(string objectType) => objectType switch
    {
        "rock_large" => 3u,
        "tree_pine"  => 4u,
        "tree_dead"  => 2u,
        _            => 1u,
    };
}
