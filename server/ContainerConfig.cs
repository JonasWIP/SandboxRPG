// server/ContainerConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>
/// Runtime registry mapping structure types to their container slot counts.
/// Populated by mod Seed() calls. Plain static dictionary — NOT a SpacetimeDB table.
/// </summary>
public static class ContainerConfig
{
    private static readonly Dictionary<string, int> _slotCounts = new();

    public static void Register(string structureType, int slotCount)
        => _slotCounts[structureType] = slotCount;

    public static int GetSlotCount(string structureType)
        => _slotCounts.TryGetValue(structureType, out var n) ? n : 0;

    public static void Clear() => _slotCounts.Clear();
}
