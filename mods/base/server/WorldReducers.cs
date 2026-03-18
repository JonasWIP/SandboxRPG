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

        uint damage    = HarvestConfig.GetToolDamage(toolType, o.ObjectType);
        uint newHealth = o.Health <= damage ? 0 : o.Health - damage;

        // Emit damage event for client-side damage numbers
        ctx.Db.DamageEvent.Insert(new DamageEvent
        {
            SourceId = 0, SourceType = "player",
            TargetId = id, TargetType = "world_object",
            Amount = (int)damage, DamageType = "harvest",
            Timestamp = NowMs(ctx),
        });

        ctx.Db.WorldObject.Delete(o);

        if (newHealth == 0)
        {
            var (dropType, dropQty) = HarvestConfig.GetDrop(o.ObjectType);
            ctx.Db.WorldItem.Insert(new WorldItem
            {
                ItemType = dropType,
                Quantity = dropQty,
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
}
