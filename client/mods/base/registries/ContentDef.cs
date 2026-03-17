// client/mods/base/registries/ContentDef.cs
using Godot;

namespace SandboxRPG;

/// <summary>Base content definition — saved as .tres for Godot inspector editing.</summary>
[GlobalClass]
public partial class ContentDef : Resource
{
    /// <summary>Path to a .glb model. Used if ScenePath is empty.</summary>
    [Export] public string ModelPath  { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public float  Scale      { get; set; } = 1.0f;
    [Export] public Color  TintColor  { get; set; } = Colors.White;
    /// <summary>
    /// Optional path to a .tscn "prefab" scene. When set, the spawner instantiates
    /// this scene directly — mesh, hitbox, and offsets are all editable in the
    /// Godot editor. Takes precedence over ModelPath.
    /// </summary>
    [Export] public string ScenePath  { get; set; } = "";
}

/// <summary>Definition for items (inventory and world drops).</summary>
[GlobalClass]
public partial class ItemDef : ContentDef
{
    [Export] public int MaxStack { get; set; } = 64;
}

/// <summary>Definition for player-placed structures.</summary>
[GlobalClass]
public partial class StructureDef : ContentDef
{
    [Export] public Vector3 CollisionSize   { get; set; } = Vector3.One;
    /// <summary>Local position of the CollisionShape3D within the body. Used to raise box shapes above ground.</summary>
    [Export] public Vector3 CollisionCenter { get; set; } = Vector3.Zero;
    [Export] public float   YOffset         { get; set; } = 0f;
    /// <summary>True → shown in BuildSystem's placement menu.</summary>
    [Export] public bool    IsPlaceable     { get; set; } = true;
}

/// <summary>Definition for harvestable world objects (trees, rocks).</summary>
[GlobalClass]
public partial class ObjectDef : ContentDef
{
    [Export] public bool UseConvexCollision { get; set; } = true;
}
