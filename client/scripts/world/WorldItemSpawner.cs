using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class WorldItemSpawner
{
    private readonly Node3D _parent;
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
            {
                kvp.Value.QueueFree();
                toRemove.Add(kvp.Key);
            }
        foreach (var id in toRemove)
            _worldItems.Remove(id);
    }

    private static Node3D CreateWorldItemVisual(WorldItem item)
    {
        var body = new StaticBody3D { Name = $"WorldItem_{item.Id}", CollisionLayer = 2, CollisionMask = 0 };

        var modelPath = ModelPath(item.ItemType);
        if (modelPath != null && ResourceLoader.Exists(modelPath))
        {
            var model = ModelRegistry.Get(modelPath)!.Instantiate<Node3D>();
            model.Position = new Vector3(0, 0.1f, 0);
            ModelRegistry.ApplyMaterials(model);
            body.AddChild(model);
        }
        else
        {
            body.AddChild(CreateFallbackMesh(item.ItemType));
        }

        body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.2f } });
        body.AddChild(new Label3D
        {
            Text        = $"{item.ItemType} x{item.Quantity}",
            FontSize    = 32,
            Billboard   = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position    = new Vector3(0, 0.5f, 0),
        });

        float groundY = Terrain.HeightAt(item.PosX, item.PosZ);
        body.Position = new Vector3(item.PosX, groundY + 0.1f, item.PosZ);
        body.SetMeta("world_item_id", (long)item.Id);
        body.SetMeta("item_type", item.ItemType);
        return body;
    }

    private static string? ModelPath(string itemType) => itemType switch
    {
        "wood"   => "res://assets/models/survival/resource-wood.glb",
        "stone"  => "res://assets/models/survival/resource-stone.glb",
        "planks" => "res://assets/models/survival/resource-planks.glb",
        _        => null,
    };

    private static MeshInstance3D CreateFallbackMesh(string itemType)
    {
        var mesh = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.4f) },
            Position = new Vector3(0, 0.2f, 0),
        };
        mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = itemType switch
            {
                "wood"  => new Color(0.6f, 0.4f, 0.2f),
                "stone" => new Color(0.5f, 0.5f, 0.55f),
                "iron"  => new Color(0.7f, 0.7f, 0.75f),
                _       => new Color(0.8f, 0.8f, 0.2f),
            },
            Roughness = 0.9f,
        };
        return mesh;
    }
}
