using Godot;
using System;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Generates the terrain mesh and collision from server-authoritative TerrainConfig.
/// Subscribe to GameManager.TerrainConfigChanged to regenerate when the server updates config.
/// HeightAt() is static so other systems (WorldManager, BuildSystem) can query ground height.
/// </summary>
public partial class Terrain : StaticBody3D
{
    [Export] public int Subdivisions = 100;
    [Export] public Material? TerrainMaterial;

    // Noise params — updated from TerrainConfig, defaults match server seed values
    private static uint  _seed      = 42;
    private static float _noiseScale = 0.04f;
    private static float _noiseAmp   = 1.5f;
    private static float _worldSize  = 500f;

    public static float WorldSize => _worldSize;

    /// <summary>World-space height at (x, z). Identical formula to server TerrainHeightAt.</summary>
    public static float HeightAt(float x, float z)
    {
        if (z < 0f) return Mathf.Max(z * 0.3f, -3f);
        float t     = Mathf.Clamp((z - 2f) / 15f, 0f, 1f);
        float baseH = 2f * t * t * (3f - 2f * t);
        float nr    = Mathf.Clamp((z - 5f) / 15f, 0f, 1f);
        float s     = _seed * 0.001f;
        float noise = (float)(
            Math.Sin(x * _noiseScale + s) * Math.Cos(z * _noiseScale * 1.7 + s * 1.3) * _noiseAmp
          + Math.Sin((x + z) * _noiseScale * 2.9 + s * 0.7) * _noiseAmp * 0.3
        );
        return baseH + noise * nr;
    }

    public override void _Ready()
    {
        GD.Print("[Terrain] _Ready called");
        var gm = GameManager.Instance;
        gm.TerrainConfigChanged += OnTerrainConfigChanged;

        var cfg = gm.GetTerrainConfig();
        if (cfg != null) ApplyConfig(cfg);

        GenerateMesh();
        GenerateCollision();
    }

    private void OnTerrainConfigChanged()
    {
        var cfg = GameManager.Instance.GetTerrainConfig();
        if (cfg == null) return;
        ApplyConfig(cfg);
        GenerateMesh();
        GenerateCollision();
    }

    private static void ApplyConfig(SpacetimeDB.Types.TerrainConfig cfg) // cfg is a class, not a struct
    {
        _seed       = cfg.Seed;
        _noiseScale = cfg.NoiseScale;
        _noiseAmp   = cfg.NoiseAmplitude;
        _worldSize  = cfg.WorldSize;
    }

    private void GenerateMesh()
    {
        var meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
        meshInstance.Mesh = BuildArrayMesh();
        if (TerrainMaterial != null)
            meshInstance.SetSurfaceOverrideMaterial(0, TerrainMaterial);
        else
            GD.PrintErr("[Terrain] No TerrainMaterial assigned!");
    }

    private void GenerateCollision()
    {
        var shape   = GetNode<CollisionShape3D>("CollisionShape3D");
        int mapSize = Subdivisions + 1;
        float step  = _worldSize / Subdivisions;
        var heights = new float[mapSize * mapSize];

        for (int z = 0; z < mapSize; z++)
        for (int x = 0; x < mapSize; x++)
            heights[z * mapSize + x] = HeightAt(x * step - _worldSize / 2f, z * step - _worldSize / 2f);

        shape.Shape = new HeightMapShape3D { MapWidth = mapSize, MapDepth = mapSize, MapData = heights };
        shape.Scale = new Vector3(step, 1f, step);
    }

    private ArrayMesh BuildArrayMesh()
    {
        int   n      = Subdivisions + 1;
        float step   = _worldSize / Subdivisions;
        float uvStep = 1f / Subdivisions;

        var positions = new List<Vector3>(n * n);
        var normals   = new List<Vector3>(n * n);
        var uvs       = new List<Vector2>(n * n);
        var indices   = new List<int>(Subdivisions * Subdivisions * 6);

        for (int z = 0; z <= Subdivisions; z++)
        for (int x = 0; x <= Subdivisions; x++)
        {
            float wx = x * step - _worldSize / 2f;
            float wz = z * step - _worldSize / 2f;
            float wy = HeightAt(wx, wz);
            positions.Add(new Vector3(wx, wy, wz));
            uvs.Add(new Vector2(x * uvStep, z * uvStep));
            normals.Add(new Vector3(wy - HeightAt(wx + 0.1f, wz), 0.1f, wy - HeightAt(wx, wz + 0.1f)).Normalized());
        }

        int w = Subdivisions + 1;
        for (int z = 0; z < Subdivisions; z++)
        for (int x = 0; x < Subdivisions; x++)
        {
            int i = z * w + x;
            indices.Add(i);         indices.Add(i + 1);     indices.Add(i + w + 1);
            indices.Add(i);         indices.Add(i + w + 1); indices.Add(i + w);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV]  = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index]  = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
