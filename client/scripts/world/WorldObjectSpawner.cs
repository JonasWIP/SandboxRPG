using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG;

public class WorldObjectSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, Node3D> _worldObjects = new();

    public WorldObjectSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    /// <summary>Initial load — adds any objects not yet in the dict. Does not remove.</summary>
    public void SyncAll()
    {
        foreach (var obj in _gm.GetAllWorldObjects())
        {
            if (!_worldObjects.ContainsKey(obj.Id))
            {
                var visual = CreateWorldObjectVisual(obj);
                _parent.AddChild(visual);
                _worldObjects[obj.Id] = visual;
            }
        }
    }

    /// <summary>Delta update from WorldObjectUpdated signal.</summary>
    public void OnUpdated(long id, bool removed)
    {
        ulong uid = (ulong)id;
        if (removed)
        {
            if (_worldObjects.TryGetValue(uid, out var existing))
            {
                existing.QueueFree();
                _worldObjects.Remove(uid);
            }
            return;
        }

        var obj = _gm.GetWorldObject(uid);
        if (obj is null) return;

        if (!_worldObjects.ContainsKey(uid))
        {
            var visual = CreateWorldObjectVisual(obj);
            _parent.AddChild(visual);
            _worldObjects[uid] = visual;
        }
    }

    private static Node3D CreateWorldObjectVisual(WorldObject obj)
    {
        var body = new StaticBody3D { Name = $"WorldObject_{obj.Id}" };

        string? modelPath = obj.ObjectType switch
        {
            "tree_pine"  => "res://assets/models/nature/tree_pineRoundA.glb",
            "tree_dead"  => "res://assets/models/nature/tree_thin_dark.glb",
            "tree_palm"  => "res://assets/models/nature/tree_palmTall.glb",
            "rock_large" => "res://assets/models/nature/rock_largeA.glb",
            "rock_small" => "res://assets/models/nature/rock_smallA.glb",
            "bush"       => "res://assets/models/nature/plant_bush.glb",
            _            => null,
        };

        float modelScale = obj.ObjectType switch
        {
            "tree_pine" or "tree_palm" => 2.5f,
            "tree_dead"                => 2.0f,
            "rock_large"               => 2.0f,
            "rock_small"               => 1.8f,
            "bush"                     => 1.5f,
            _                          => 1.0f,
        };

        if (modelPath != null && ResourceLoader.Exists(modelPath))
        {
            var model = ModelRegistry.Get(modelPath)!.Instantiate<Node3D>();
            model.Scale = Vector3.One * modelScale;
            Color? tint = obj.ObjectType is "rock_large" or "rock_small" ? new Color(0.6f, 0.6f, 0.6f) : null;
            ModelRegistry.ApplyMaterials(model, tint);
            body.AddChild(model);
            body.AddChild(new CollisionShape3D { Shape = BuildConvexShape(model, modelScale) });
        }
        else
        {
            body.AddChild(new MeshInstance3D
            {
                Mesh     = new BoxMesh { Size = new Vector3(0.8f, 1.5f, 0.8f) * modelScale },
                Position = new Vector3(0, 0.75f * modelScale, 0),
            });
            var shapeSize = obj.ObjectType switch
            {
                "tree_pine" or "tree_palm" => new Vector3(1.2f, 6.0f, 1.2f),
                "tree_dead"                => new Vector3(1.0f, 5.0f, 1.0f),
                "rock_large"               => new Vector3(2.4f, 1.6f, 2.4f),
                "rock_small"               => new Vector3(1.1f, 0.7f, 1.1f),
                "bush"                     => new Vector3(1.5f, 1.0f, 1.5f),
                _                          => new Vector3(0.8f, 1.0f, 0.8f),
            };
            body.AddChild(new CollisionShape3D
            {
                Shape    = new BoxShape3D { Size = shapeSize },
                Position = new Vector3(0, shapeSize.Y / 2f, 0),
            });
        }

        float groundY = Terrain.HeightAt(obj.PosX, obj.PosZ);
        body.Position = new Vector3(obj.PosX, groundY, obj.PosZ);
        body.Rotation = new Vector3(0, obj.RotY, 0);
        body.AddToGroup("world_object");
        body.SetMeta("world_object_id", (long)obj.Id);
        body.SetMeta("object_type", obj.ObjectType);

        return body;
    }

    private static ConvexPolygonShape3D BuildConvexShape(Node3D model, float scale)
    {
        var pts = new List<Vector3>();
        foreach (var mi in model.FindChildren("*", "MeshInstance3D", owned: false).OfType<MeshInstance3D>())
        {
            if (mi.Mesh is not ArrayMesh arrayMesh) continue;
            var shape = arrayMesh.CreateConvexShape(clean: true, simplify: false);
            pts.AddRange(shape.Points);
        }
        for (int i = 0; i < pts.Count; i++)
            pts[i] *= scale;
        return new ConvexPolygonShape3D { Points = pts.ToArray() };
    }
}
