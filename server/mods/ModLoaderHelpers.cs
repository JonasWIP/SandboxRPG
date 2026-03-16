// server/mods/ModLoaderHelpers.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG.Server.Mods;

/// <summary>Pure TopoSort with no SpacetimeDB dependency — testable in isolation.</summary>
public static class ModLoaderHelpers
{
    /// <summary>
    /// Topological sort (Kahn's algorithm).
    /// Throws InvalidOperationException on circular dependency.
    /// Unknown dependency names are silently ignored.
    /// </summary>
    public static List<T> TopoSort<T>(
        List<T> items,
        Func<T, string>   getName,
        Func<T, string[]> getDeps)
    {
        var nameToItem = items.ToDictionary(getName);
        var inDegree   = items.ToDictionary(getName, _ => 0);

        foreach (var item in items)
            foreach (var dep in getDeps(item))
                if (nameToItem.ContainsKey(dep))
                    inDegree[getName(item)]++;

        var queue  = new Queue<T>(items.Where(i => inDegree[getName(i)] == 0));
        var result = new List<T>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var dependent in items.Where(i => getDeps(i).Contains(getName(current))))
            {
                inDegree[getName(dependent)]--;
                if (inDegree[getName(dependent)] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count != items.Count)
            throw new InvalidOperationException("[ModLoader] Circular dependency detected in mods.");

        return result;
    }
}
