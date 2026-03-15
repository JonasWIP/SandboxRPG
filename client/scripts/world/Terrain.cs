using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Generates the coastal terrain mesh and collision at runtime.
/// Beach (Z ≤ 5) is flat at Y=0; smooth rise to Y=4 by Z=25; plateau beyond.
/// Must be a child of (or itself be) a StaticBody3D with a MeshInstance3D and
/// CollisionShape3D child — see Task 13 for scene setup.
/// </summary>
public partial class Terrain : StaticBody3D
{
    [Export] public int Subdivisions = 50;   // quads per axis
    [Export] public float WorldSize  = 100f;
    [Export] public float MaxHeight  = 4f;
    [Export] public float BeachEnd   = 5f;
    [Export] public float SlopeWidth = 20f;
    [Export] public Material? TerrainMaterial;

    /// <summary>Height at world position (X, Z). Used by other systems for Y placement.</summary>
    public static float HeightAt(float x, float z)
    {
        float t = Mathf.Clamp((z - 5f) / 20f, 0f, 1f);
        return Mathf.SmoothStep(0f, 4f, t);
    }

    public override void _Ready()
    {
        GD.Print("[Terrain] _Ready called");
        GenerateMesh();
        GenerateCollision();
    }

    private void GenerateMesh()
    {
        var meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
        meshInstance.Mesh = BuildArrayMesh();

        // Use exported material if set, otherwise load shader from path
        Material? mat = TerrainMaterial;
        if (mat == null)
        {
            const string shaderPath = "res://assets/shaders/terrain_blend.gdshader";
            if (ResourceLoader.Exists(shaderPath))
            {
                var shader = ResourceLoader.Load<Shader>(shaderPath);
                mat = new ShaderMaterial { Shader = shader };
            }
        }

        if (mat != null)
            meshInstance.SetSurfaceOverrideMaterial(0, mat);
        else
            GD.PrintErr("[Terrain] No material could be applied!");
    }

    private void GenerateCollision()
    {
        var shape = GetNode<CollisionShape3D>("CollisionShape3D");

        int mapSize = Subdivisions + 1;
        float[] heights = new float[mapSize * mapSize];
        float step = WorldSize / Subdivisions;

        for (int z = 0; z < mapSize; z++)
        for (int x = 0; x < mapSize; x++)
        {
            float worldX = x * step - WorldSize / 2f;
            float worldZ = z * step - WorldSize / 2f;
            heights[z * mapSize + x] = HeightAt(worldX, worldZ);
        }

        var heightmap = new HeightMapShape3D();
        heightmap.MapWidth  = mapSize;
        heightmap.MapDepth  = mapSize;
        heightmap.MapData   = heights;
        shape.Shape = heightmap;

        // HeightMapShape3D is centred at origin; scale to match world size
        shape.Scale = new Vector3(WorldSize / Subdivisions, 1f, WorldSize / Subdivisions);
    }

    private ArrayMesh BuildArrayMesh()
    {
        int verts = (Subdivisions + 1) * (Subdivisions + 1);
        var positions = new List<Vector3>(verts);
        var normals   = new List<Vector3>(verts);
        var uvs       = new List<Vector2>(verts);
        var indices   = new List<int>(Subdivisions * Subdivisions * 6);

        float step = WorldSize / Subdivisions;
        float uvStep = 1f / Subdivisions;

        for (int z = 0; z <= Subdivisions; z++)
        for (int x = 0; x <= Subdivisions; x++)
        {
            float wx = x * step - WorldSize / 2f;
            float wz = z * step - WorldSize / 2f;
            float wy = HeightAt(wx, wz);

            positions.Add(new Vector3(wx, wy, wz));
            uvs.Add(new Vector2(x * uvStep, z * uvStep));

            // Approximate normal via finite difference
            float hR = HeightAt(wx + 0.1f, wz);
            float hF = HeightAt(wx, wz + 0.1f);
            normals.Add(new Vector3(wy - hR, 0.1f, wy - hF).Normalized());
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
