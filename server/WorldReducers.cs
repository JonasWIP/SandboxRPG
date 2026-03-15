using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void DamageWorldObject(ReducerContext ctx, ulong id, uint damage)
    {
        var obj = ctx.Db.WorldObject.Id.Find(id);
        if (obj is null) return;
        var o = obj.Value;

        uint newHealth = o.Health <= damage ? 0 : o.Health - damage;

        // STDB 2.0: no UpdateByField — delete then reinsert
        ctx.Db.WorldObject.Delete(o);

        if (newHealth == 0)
        {
            // Drop items at the object's position
            ctx.Db.WorldItem.Insert(new WorldItem
            {
                ItemType = DropTypeFor(o.ObjectType),
                Quantity = 1,
                PosX = o.PosX,
                PosY = o.PosY,
                PosZ = o.PosZ,
            });
        }
        else
        {
            // Reinsert with updated health. Note: AutoInc assigns a new Id on
            // each insert — the client will see a delete+insert pair per hit.
            ctx.Db.WorldObject.Insert(new WorldObject
            {
                ObjectType = o.ObjectType,
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
                RotY = o.RotY,
                Health = newHealth,
                MaxHealth = o.MaxHealth,
            });
        }
    }

    private static string DropTypeFor(string objectType) => objectType switch
    {
        "rock_large" or "rock_small" => "stone",
        _ => "wood",    // trees, stumps, bushes
    };
}
