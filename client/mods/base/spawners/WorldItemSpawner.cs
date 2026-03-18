// client/mods/base/spawners/WorldItemSpawner.cs
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class WorldItemSpawner
{
    private readonly Node3D      _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, Node3D> _worldItems = new();

    public WorldItemSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    public void Sync()
    {
        var currentIds = new HashSet<ulong>();
        foreach (var item in _gm.GetAllWorldItems())
        {
            currentIds.Add(item.Id);
            if (!_worldItems.ContainsKey(item.Id))
            {
                var visual = CreateWorldItemVisual(item);
                _parent.AddChild(visual);
                _worldItems[item.Id] = visual;
            }
        }
        var toRemove = new List<ulong>();
        foreach (var kvp in _worldItems)
            if (!currentIds.Contains(kvp.Key))
            { kvp.Value.QueueFree(); toRemove.Add(kvp.Key); }
        foreach (var id in toRemove) _worldItems.Remove(id);
    }

    private static Node3D CreateWorldItemVisual(WorldItem item)
    {
        var body = new WorldItemPickup
        {
            Name = $"WorldItem_{item.Id}",
            CollisionLayer = 2,
            CollisionMask = 0,
            WorldItemId = item.Id,
            ItemType = item.ItemType,
            Quantity = item.Quantity,
        };
        var def = ItemRegistry.Get(item.ItemType);
        var visual = ContentSpawner.SpawnVisual(def, item.ItemType);
        float itemScale = def?.Scale ?? 0.4f;
        visual.Position = new Vector3(0, 0.1f, 0);
        body.AddChild(visual);
        body.AddChild(new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = Mathf.Max(0.3f, itemScale) },
            Position = new Vector3(0, 0.1f + itemScale * 0.5f, 0),
        });
        body.AddChild(new Label3D
        {
            Text = $"{item.ItemType} x{item.Quantity}", FontSize = 32,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            Position  = new Vector3(0, 0.5f, 0),
        });
        float groundY = Terrain.HeightAt(item.PosX, item.PosZ);
        body.Position = new Vector3(item.PosX, groundY + 0.1f, item.PosZ);
        return body;
    }
}
