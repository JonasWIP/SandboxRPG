// client/mods/base/spawners/ContentSpawner.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Shared visual-spawning helper used by all spawners.
/// Priority: ScenePath → ModelPath → fallback coloured box.
/// </summary>
public static class ContentSpawner
{
    public static Node3D SpawnVisual(ContentDef? def, string typeFallback)
    {
        // Priority: ScenePath (full prefab) → ModelPath (generated) → coloured fallback box
        if (def is not null && !string.IsNullOrEmpty(def.ScenePath) && ResourceLoader.Exists(def.ScenePath))
            return ResourceLoader.Load<PackedScene>(def.ScenePath).Instantiate<Node3D>();

        if (def is not null && !string.IsNullOrEmpty(def.ModelPath) && ResourceLoader.Exists(def.ModelPath))
        {
            var model = ModelRegistry.Get(def.ModelPath)!.Instantiate<Node3D>();
            model.Scale = Vector3.One * def.Scale;
            Color? tint = def.TintColor != Colors.White ? def.TintColor : null;
            ModelRegistry.ApplyMaterials(model, tint);  // pass tint so rock/wood/stone tints apply
            return model;
        }

        return CreateFallbackMesh(def?.TintColor ?? new Color(0.8f, 0.8f, 0.2f), def?.Scale ?? 0.4f);
    }

    private static MeshInstance3D CreateFallbackMesh(Color color, float size)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size, size, size) },
            Position = new Vector3(0, size * 0.5f, 0),
        };
        mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f };
        return mesh;
    }
}
