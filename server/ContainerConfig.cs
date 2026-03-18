// server/ContainerConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>
/// Runtime registry of container types and their slot capacities.
/// Populated during mod Seed(); repopulated on every Init.
/// </summary>
public static class ContainerConfig
{
    private static readonly Dictionary<string, int> _slotCounts = new();

    public static void Register(string containerType, int slotCount)
        => _slotCounts[containerType] = slotCount;

    public static int GetSlotCount(string containerType)
        => _slotCounts.TryGetValue(containerType, out var n) ? n : 0;

    public static void Clear() => _slotCounts.Clear();
}
