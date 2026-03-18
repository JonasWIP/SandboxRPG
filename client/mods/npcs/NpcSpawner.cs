// client/mods/npcs/NpcSpawner.cs
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class NpcSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, NpcEntity> _npcs = new();

    public NpcSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm = gm;
    }

    public void SpawnAll()
    {
        foreach (var npc in _gm.GetAllNpcs())
        {
            if (!_npcs.ContainsKey(npc.Id))
                Spawn(npc);
        }
    }

    public void OnUpdated(long id, bool removed)
    {
        ulong uid = (ulong)id;
        if (removed)
        {
            Remove(uid);
            return;
        }

        var npc = _gm.GetNpc(uid);
        if (npc is null) return;

        if (_npcs.TryGetValue(uid, out var existing))
        {
            existing.UpdateFromServer(npc);
        }
        else
        {
            Spawn(npc);
        }
    }

    private void Spawn(Npc npc)
    {
        var entity = new NpcEntity
        {
            Name = $"Npc_{npc.NpcType}_{npc.Id}",
            NpcId = npc.Id,
            NpcType = npc.NpcType,
            NpcIsAlive = npc.IsAlive,
            NpcHealth = npc.Health,
            NpcMaxHealth = npc.MaxHealth,
        };
        _parent.AddChild(entity);

        float groundY = Terrain.HeightAt(npc.PosX, npc.PosZ);
        float y = npc.PosY > 0.01f ? npc.PosY : groundY;
        entity.GlobalPosition = new Vector3(npc.PosX, y, npc.PosZ);
        entity.Rotation = new Vector3(0, npc.RotY, 0);

        _npcs[npc.Id] = entity;
        GD.Print($"[NpcSpawner] Spawned {npc.NpcType} (id={npc.Id})");
    }

    private void Remove(ulong id)
    {
        if (_npcs.TryGetValue(id, out var entity))
        {
            entity.QueueFree();
            _npcs.Remove(id);
        }
    }
}
