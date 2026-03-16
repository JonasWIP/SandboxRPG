using Godot;

namespace SandboxRPG;

/// <summary>
/// Thin coordinator — wires GameManager signals to the spawner classes.
/// </summary>
public partial class WorldManager : Node3D
{
    private bool _worldSpawned;

    private PlayerSpawner _players = null!;
    private WorldItemSpawner _items = null!;
    private StructureSpawner _structures = null!;
    private WorldObjectSpawner _worldObjects = null!;

    public override void _Ready()
    {
        var gm = GameManager.Instance;
        _players      = new PlayerSpawner(this, gm);
        _items        = new WorldItemSpawner(this, gm);
        _structures   = new StructureSpawner(this, gm);
        _worldObjects = new WorldObjectSpawner(this, gm);

        gm.SubscriptionApplied += OnSubscriptionApplied;
        gm.PlayerUpdated       += id => _players.OnUpdated(id);
        gm.PlayerRemoved       += id => _players.OnRemoved(id);
        gm.WorldItemChanged    += _items.Sync;
        gm.StructureChanged    += _structures.Sync;
        gm.WorldObjectUpdated  += _worldObjects.OnUpdated;

        if (gm.IsConnected && gm.GetLocalPlayer() != null)
            OnSubscriptionApplied();

        ModManager.Instance.InitializeAll(this);
    }

    private void OnSubscriptionApplied()
    {
        if (_worldSpawned) return;
        _worldSpawned = true;
        GD.Print("[WorldManager] Initial data received, spawning world...");
        _players.SpawnAll();
        _items.Sync();
        _structures.Sync();
        _worldObjects.SyncAll();
    }
}
