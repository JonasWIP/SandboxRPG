using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Caches loaded PackedScenes by path so each GLB is read from disk only once.
/// Also owns ApplyMaterials since it is the sole consumer of material overrides.
/// </summary>
public static class ModelRegistry
{
    private static readonly Dictionary<string, PackedScene> _cache = new();

    /// <summary>
    /// Returns the cached PackedScene for <paramref name="path"/>.
    /// Caller must check <c>ResourceLoader.Exists(path)</c> before calling —
    /// if the asset is missing ResourceLoader.Load returns null, which is cached.
    /// </summary>
    public static PackedScene? Get(string path)
    {
        if (!_cache.TryGetValue(path, out var scene))
            _cache[path] = scene = ResourceLoader.Load<PackedScene>(path);
        return scene;
    }

    /// <summary>
    /// Recursively processes every MeshInstance3D surface under <paramref name="root"/>:
    /// duplicates the material, zeroes Metallic, then either applies <paramref name="color"/>
    /// or dims AlbedoColor by 0.85.
    /// </summary>
    public static void ApplyMaterials(Node root, Color? color = null)
    {
        if (root is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int surf = 0; surf < mi.Mesh.GetSurfaceCount(); surf++)
            {
                var mat = mi.GetActiveMaterial(surf);
                if (mat is not BaseMaterial3D bm) continue;
                var dup = (BaseMaterial3D)bm.Duplicate();
                dup.Metallic    = 0f;
                dup.AlbedoColor = color ?? dup.AlbedoColor * 0.85f;
                mi.SetSurfaceOverrideMaterial(surf, dup);
            }
        }
        foreach (Node child in root.GetChildren())
            ApplyMaterials(child, color);
    }
}
