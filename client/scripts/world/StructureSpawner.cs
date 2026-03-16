using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class StructureSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, StaticBody3D> _structures = new();

    public StructureSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    public void Sync()
    {
        var currentIds = new HashSet<ulong>();

        foreach (var structure in _gm.GetAllStructures())
        {
            currentIds.Add(structure.Id);
            if (!_structures.ContainsKey(structure.Id))
            {
                var visual = CreateStructureVisual(structure);
                _parent.AddChild(visual);
                _structures[structure.Id] = visual;
            }
        }

        var toRemove = new List<ulong>();
        foreach (var kvp in _structures)
            if (!currentIds.Contains(kvp.Key))
            {
                kvp.Value.QueueFree();
                toRemove.Add(kvp.Key);
            }
        foreach (var id in toRemove)
            _structures.Remove(id);
    }

    private static StaticBody3D CreateStructureVisual(PlacedStructure structure)
    {
        var body = new StaticBody3D { Name = $"Structure_{structure.Id}", CollisionLayer = 1, CollisionMask = 1 };

        string? modelPath = StructureModelPath(structure.StructureType);
        if (modelPath != null && ResourceLoader.Exists(modelPath))
        {
            var visual = ModelRegistry.Get(modelPath)!.Instantiate<Node3D>();
            Color? tint = structure.StructureType switch
            {
                "wood_wall" or "wood_floor" or "wood_door" => new Color(1.0f, 0.78f, 0.55f),
                "stone_wall" or "stone_floor"              => new Color(0.82f, 0.82f, 0.88f),
                _                                          => null,
            };
            ModelRegistry.ApplyMaterials(visual, tint);
            body.AddChild(visual);
        }
        else
        {
            bool isStone = structure.StructureType.Contains("stone");
            var mesh = new MeshInstance3D { Mesh = StructureFallbackMesh(structure.StructureType) };
            mesh.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = structure.StructureType switch
                {
                    "campfire"     => new Color(0.8f, 0.3f,  0.1f),
                    "workbench"    => new Color(0.5f, 0.35f, 0.2f),
                    "chest"        => new Color(0.55f, 0.4f, 0.25f),
                    _ when isStone => new Color(0.55f, 0.55f, 0.6f),
                    _              => new Color(0.6f, 0.45f, 0.25f),
                },
                Roughness = 0.85f,
            };
            mesh.Position = new Vector3(0, StructureYOffset(structure.StructureType), 0);
            body.AddChild(mesh);
        }

        var (sz, sc) = GetBoxShape(structure.StructureType);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = sz }, Position = sc });
        body.Position = new Vector3(structure.PosX, structure.PosY, structure.PosZ);
        body.Rotation = new Vector3(0, structure.RotY, 0);
        body.SetMeta("structure_id",   (long)structure.Id);
        body.SetMeta("structure_type", structure.StructureType);
        body.SetMeta("owner_id",       structure.OwnerId.ToString());

        return body;
    }

    // ── Public static lookup tables (also used by BuildSystem) ────────────────

    public static string? StructureModelPath(string t) => t switch
    {
        "wood_wall"  or "stone_wall"  => "res://assets/models/building/wall.glb",
        "wood_floor" or "stone_floor" => "res://assets/models/building/floor.glb",
        "wood_door"                   => "res://assets/models/building/wall-doorway-square.glb",
        "campfire"                    => "res://assets/models/survival/campfire-pit.glb",
        "workbench"                   => "res://assets/models/survival/workbench.glb",
        "chest"                       => "res://assets/models/survival/chest.glb",
        _                             => null,
    };

    public static Mesh StructureFallbackMesh(string t) => t switch
    {
        "wood_wall" or "stone_wall"   => new BoxMesh { Size = new Vector3(2f, 2.5f, 0.2f) },
        "wood_floor" or "stone_floor" => new BoxMesh { Size = new Vector3(2f, 0.1f, 2f) },
        "wood_door"                   => new BoxMesh { Size = new Vector3(1f, 2.2f, 0.15f) },
        "campfire"                    => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 0.3f },
        "workbench"                   => new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) },
        "chest"                       => new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.5f) },
        _                             => new BoxMesh { Size = new Vector3(1f, 1f, 1f) },
    };

    public static float StructureYOffset(string t) => t switch
    {
        "wood_wall" or "stone_wall"   => 1.25f,
        "wood_floor" or "stone_floor" => 0.05f,
        "wood_door"                   => 1.1f,
        "campfire"                    => 0.15f,
        "workbench"                   => 0.4f,
        "chest"                       => 0.3f,
        _                             => 0.5f,
    };

    private static (Vector3 size, Vector3 center) GetBoxShape(string t) => t switch
    {
        "wood_wall"  or "stone_wall"  => (new Vector3(0.25f, 2.4f, 2.0f), new Vector3(0, 1.2f, 0)),
        "wood_floor" or "stone_floor" => (new Vector3(2.0f,  0.1f, 2.0f), new Vector3(0, 0.05f, 0)),
        "wood_door"                   => (new Vector3(0.25f, 2.4f, 2.0f), new Vector3(0, 1.2f, 0)),
        "campfire"                    => (new Vector3(0.8f,  0.4f, 0.8f),  new Vector3(0, 0.2f,  0)),
        "workbench"                   => (new Vector3(1.2f,  0.8f, 0.6f),  new Vector3(0, 0.4f,  0)),
        "chest"                       => (new Vector3(0.8f,  0.6f, 0.6f),  new Vector3(0, 0.3f,  0)),
        _                             => (new Vector3(1.0f,  1.0f, 1.0f),  new Vector3(0, 0.5f,  0)),
    };
}
