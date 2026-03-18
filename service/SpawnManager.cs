// service/SpawnManager.cs
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public class SpawnManager
{
    private readonly Func<IEnumerable<NpcSpawnRule>> _getSpawnRules;
    private readonly Func<IEnumerable<Npc>> _getNpcs;
    private readonly Action<string, float, float, float, float> _spawnNpc;
    private readonly Action<ulong> _respawnNpc;
    private readonly Random _rng = new();

    // Track death times for respawn delay
    private readonly Dictionary<ulong, DateTimeOffset> _deathTimes = new();
    // Track pending spawns to avoid over-spawning before subscription confirms
    private readonly Dictionary<string, int> _pendingSpawns = new();

    public SpawnManager(
        Func<IEnumerable<NpcSpawnRule>> getSpawnRules,
        Func<IEnumerable<Npc>> getNpcs,
        Action<string, float, float, float, float> spawnNpc,
        Action<ulong> respawnNpc)
    {
        _getSpawnRules = getSpawnRules;
        _getNpcs = getNpcs;
        _spawnNpc = spawnNpc;
        _respawnNpc = respawnNpc;
    }

    public void Tick()
    {
        var npcs = _getNpcs().ToList();
        var now = DateTimeOffset.UtcNow;

        foreach (var rule in _getSpawnRules())
        {
            // Count alive NPCs of this type in this zone
            int aliveCount = 0;
            var deadInZone = new List<Npc>();
            string ruleKey = $"{rule.NpcType}_{rule.ZoneX}_{rule.ZoneZ}";
            int pending = _pendingSpawns.GetValueOrDefault(ruleKey, 0);

            foreach (var npc in npcs)
            {
                if (npc.NpcType != rule.NpcType) continue;
                float dx = npc.SpawnPosX - rule.ZoneX;
                float dz = npc.SpawnPosZ - rule.ZoneZ;
                float distSq = dx * dx + dz * dz;
                float maxDist = rule.ZoneRadius > 0 ? rule.ZoneRadius + 5f : 5f;
                if (distSq > maxDist * maxDist) continue;

                if (npc.IsAlive)
                    aliveCount++;
                else
                    deadInZone.Add(npc);
            }

            // Reduce pending count as NPCs appear
            if (pending > 0 && aliveCount > 0)
            {
                int confirmed = Math.Min(pending, aliveCount);
                _pendingSpawns[ruleKey] = pending - confirmed;
                pending = _pendingSpawns[ruleKey];
            }
            aliveCount += pending; // include pending in count to prevent over-spawn

            // Respawn dead NPCs after delay
            foreach (var dead in deadInZone)
            {
                if (aliveCount >= rule.MaxCount) break;

                if (!_deathTimes.ContainsKey(dead.Id))
                {
                    _deathTimes[dead.Id] = now;
                    continue;
                }

                if ((now - _deathTimes[dead.Id]).TotalSeconds >= rule.RespawnTimeSec)
                {
                    _respawnNpc(dead.Id);
                    _deathTimes.Remove(dead.Id);
                    aliveCount++;
                }
            }

            // Spawn new NPCs if under max and no dead to respawn
            while (aliveCount < rule.MaxCount)
            {
                float x, z;
                if (rule.ZoneRadius > 0)
                {
                    float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                    float dist = (float)(_rng.NextDouble() * rule.ZoneRadius);
                    x = rule.ZoneX + MathF.Cos(angle) * dist;
                    z = rule.ZoneZ + MathF.Sin(angle) * dist;
                }
                else
                {
                    x = rule.ZoneX;
                    z = rule.ZoneZ;
                }

                float rotY = (float)(_rng.NextDouble() * Math.PI * 2);
                _spawnNpc(rule.NpcType, x, 0f, z, rotY);
                _pendingSpawns[ruleKey] = _pendingSpawns.GetValueOrDefault(ruleKey, 0) + 1;
                aliveCount++;
            }
        }
    }
}
