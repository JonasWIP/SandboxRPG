// client/mods/base/spawners/StructureSpawner.cs
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class StructureSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, Node3D> _structures = new();

    private static readonly HashSet<string> InteractableTypes = new()
    {
        "chest",
        "furnace",
        "crafting_table",
        "sign",
    };

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

    private static bool IsInteractable(string structureType) =>
        InteractableTypes.Contains(structureType);

    private static Node3D CreateStructureVisual(PlacedStructure s)
    {
        var def  = StructureRegistry.Get(s.StructureType);

        StaticBody3D body;
        if (IsInteractable(s.StructureType))
        {
            var interactable = new InteractableStructure
            {
                Name          = $"Structure_{s.Id}",
                CollisionLayer = 1,
                CollisionMask  = 1,
                StructureId   = s.Id,
                StructureType = s.StructureType,
                OwnerId       = s.OwnerId,
            };
            body = interactable;
        }
        else
        {
            body = new StaticBody3D { Name = $"Structure_{s.Id}", CollisionLayer = 1, CollisionMask = 1 };
        }

        var visual = ContentSpawner.SpawnVisual(def, s.StructureType);
        body.AddChild(visual);

        // CollisionCenter lifts the box shape so walls/floors sit correctly above placement point
        var collSize   = def?.CollisionSize   ?? Vector3.One;
        var collCenter = def?.CollisionCenter ?? Vector3.Zero;
        body.AddChild(new CollisionShape3D
        {
            Shape    = new BoxShape3D { Size = collSize },
            Position = collCenter,  // e.g. (0, 1.2, 0) for walls so box is above ground
        });

        body.Position = new Vector3(s.PosX, s.PosY, s.PosZ);
        body.Rotation = new Vector3(0, s.RotY, 0);
        body.SetMeta("structure_id",   (long)s.Id);
        body.SetMeta("structure_type", s.StructureType);
        body.SetMeta("owner_id",       s.OwnerId.ToString());
        return body;
    }
}
