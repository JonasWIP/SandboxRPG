// server/AccessControl.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    /// <summary>String constants for entity table cross-references.</summary>
    public static class EntityTables
    {
        public const string PlacedStructure = "placed_structure";
        public const string WorldObject = "world_object";
    }

    [Table(Name = "access_control", Public = true)]
    public partial struct AccessControl
    {
        [AutoInc][PrimaryKey] public ulong Id;
        public ulong EntityId;
        public string EntityTable;
        public Identity OwnerId;
        public bool IsPublic;
    }

    public static class AccessControlHelper
    {
        public static bool CanAccess(ReducerContext ctx, ulong entityId, string entityTable)
        {
            foreach (var ac in ctx.Db.AccessControl.Iter())
            {
                if (ac.EntityId == entityId && ac.EntityTable == entityTable)
                {
                    if (ac.IsPublic) return true;
                    return ac.OwnerId == ctx.Sender;
                }
            }
            return true;
        }

        public static AccessControl? Find(ReducerContext ctx, ulong entityId, string entityTable)
        {
            foreach (var ac in ctx.Db.AccessControl.Iter())
                if (ac.EntityId == entityId && ac.EntityTable == entityTable)
                    return ac;
            return null;
        }
    }

    [Reducer]
    public static void ToggleAccessControl(ReducerContext ctx, ulong entityId, string entityTable)
    {
        var ac = AccessControlHelper.Find(ctx, entityId, entityTable);
        if (ac is null)
            throw new System.Exception("No access control entry found.");

        var row = ac.Value;
        if (row.OwnerId != ctx.Sender)
            throw new System.Exception("Only the owner can toggle access control.");

        row.IsPublic = !row.IsPublic;
        ctx.Db.AccessControl.Id.Update(row);
        Log.Info($"Access control toggled: entity {entityId} in {entityTable} -> IsPublic={row.IsPublic}");
    }
}
