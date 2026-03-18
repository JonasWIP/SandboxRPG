// service/GameService.cs
using SpacetimeDB;
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public static class GameService
{
    private static DbConnection? _conn;
    private static bool _subscriptionApplied;
    private static readonly Dictionary<ulong, NpcBrain> _brains = new();
    private static SpawnManager? _spawnManager;
    private static ulong _lastCleanupMs;

    private const int TickRateMs = 100; // 10 ticks/sec

    public static async Task Main(string[] args)
    {
        string url = args.Length > 0 ? args[0] : "http://127.0.0.1:3000";
        string dbName = args.Length > 1 ? args[1] : "sandbox-rpg";

        Console.WriteLine($"[GameService] Connecting to {url} database={dbName}...");

        // Register built-in systems
        BehaviorRegistry.RegisterBuiltIns();
        ConditionRegistry.RegisterBuiltIns();

        // Register and init service mods
        ServiceModLoader.Register(new NpcServiceMod());
        ServiceModLoader.InitializeAll(new ServiceContext());

        // Connect to SpacetimeDB
        _conn = DbConnection.Builder()
            .WithUri(url)
            .WithDatabaseName(dbName)
            .OnConnect(OnConnected)
            .OnConnectError(OnConnectError)
            .OnDisconnect(OnDisconnected)
            .Build();

        // Main loop
        while (true)
        {
            _conn.FrameTick();

            if (_subscriptionApplied)
            {
                Tick();
            }

            await Task.Delay(TickRateMs);
        }
    }

    private static void OnConnected(DbConnection conn, Identity identity, string token)
    {
        Console.WriteLine($"[GameService] Connected! Identity: {identity}");

        // Register as service
        conn.Reducers.RegisterServiceIdentity();

        conn.SubscriptionBuilder()
            .OnApplied(_ =>
            {
                Console.WriteLine("[GameService] Subscription applied.");
                _subscriptionApplied = true;
                InitSpawnManager();
            })
            .OnError((_, err) => Console.WriteLine($"[GameService] Sub error: {err}"))
            .SubscribeToAllTables();
    }

    private static void OnConnectError(Exception err)
    {
        Console.WriteLine($"[GameService] Connection error: {err.Message}");
    }

    private static void OnDisconnected(DbConnection conn, Exception? err)
    {
        Console.WriteLine($"[GameService] Disconnected: {err?.Message ?? "clean"}");
        _subscriptionApplied = false;
    }

    private static void InitSpawnManager()
    {
        _spawnManager = new SpawnManager(
            () => _conn!.Db.NpcSpawnRule.Iter(),
            () => _conn!.Db.Npc.Iter(),
            (type, x, y, z, rotY) => _conn!.Reducers.SpawnNpc(type, x, y, z, rotY),
            (id) => _conn!.Reducers.NpcRespawn(id)
        );
    }

    private static void Tick()
    {
        float delta = TickRateMs / 1000f;

        // Spawn/respawn NPCs
        _spawnManager?.Tick();

        // Update brains
        var currentNpcs = new HashSet<ulong>();
        foreach (var npc in _conn!.Db.Npc.Iter())
        {
            currentNpcs.Add(npc.Id);

            if (!npc.IsAlive) continue;

            if (!_brains.TryGetValue(npc.Id, out var brain))
            {
                var config = NpcConfigRegistry.Get(npc.NpcType);
                if (config == null) continue;

                brain = new NpcBrain(npc, config,
                    () => _conn.Db.Player.Iter(),
                    (id, x, y, z, r) => _conn.Reducers.NpcMove(id, x, y, z, r),
                    (id, s, tid, tt) => _conn.Reducers.NpcSetState(id, s, tid, tt),
                    (id, tid, tt, amt, dt) => _conn.Reducers.NpcDealDamage(id, tid, tt, amt, dt),
                    (id, tHex, amt, dt) => _conn.Reducers.NpcDealDamageToPlayer(id, tHex, amt, dt)
                );
                _brains[npc.Id] = brain;
            }
            else
            {
                brain.UpdateFromServer(npc);
            }

            brain.Tick(delta);
        }

        // Cleanup brains for deleted NPCs
        var toRemove = _brains.Keys.Where(id => !currentNpcs.Contains(id)).ToList();
        foreach (var id in toRemove) _brains.Remove(id);

        // Periodic damage event cleanup (every 30 seconds)
        ulong nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs - _lastCleanupMs > 30_000)
        {
            _conn.Reducers.CleanupDamageEvents();
            _lastCleanupMs = nowMs;
        }
    }
}
