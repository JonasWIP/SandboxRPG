// mods/base/server/StructureConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>
/// Runtime registry populated by BaseMod.Seed (and any dependent mod's Seed).
/// Replaces the hard-coded switch in BuildingReducers.PlaceStructure.
/// This is a plain static dictionary — NOT a SpacetimeDB table. Repopulated on every Init.
/// </summary>
public static class StructureConfig
{
    private static readonly Dictionary<string, float> _maxHealth = new();

    public static void Register(string structureType, float maxHealth)
        => _maxHealth[structureType] = maxHealth;

    public static float GetMaxHealth(string structureType)
        => _maxHealth.TryGetValue(structureType, out var h) ? h : 100f;
}
