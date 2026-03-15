# Physics and Visual Fixes Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix convex hull hitboxes for world objects, ghost preview rotation mismatch, item fall-through, water coverage, and wall stacking via collision.

**Architecture:** Five independent client-only changes in `WorldManager.cs`, `BuildSystem.cs`, and `Main.tscn`. No server changes required. Tasks 1–4 can each be verified by a successful `dotnet build`; Task 5 requires a quick in-game check.

**Tech Stack:** Godot 4.6.1 C#, .NET 8

---

## File Structure

| File | What changes |
|------|-------------|
| `client/scripts/world/WorldManager.cs` | Tasks 1, 2, 3, 5 — four independent edits |
| `client/scripts/building/BuildSystem.cs` | Task 2 — ghost preview loads real GLB |
| `client/scenes/Main.tscn` | Task 4 — Ocean node resize/reposition |

---

## Task 1: Convex Hull Hitboxes for World Objects

**Files:**
- Modify: `client/scripts/world/WorldManager.cs` — `CreateWorldObjectVisual` (lines 393–459) and add two new private helpers

No automated tests exist — verify by building the project.

- [ ] **Step 1: Add two private helper methods**

Add these two methods inside the `WorldManager` class, just before `CreateWorldObjectVisual`:

```csharp
private static ConvexPolygonShape3D BuildConvexShape(Node3D model, float scale)
{
    var verts = new List<Vector3>();
    CollectFaces(model, verts);
    for (int i = 0; i < verts.Count; i++)
        verts[i] *= scale;
    return new ConvexPolygonShape3D { Points = verts.ToArray() };
}

private static void CollectFaces(Node node, List<Vector3> verts)
{
    if (node is MeshInstance3D mi && mi.Mesh != null)
        verts.AddRange(mi.Mesh.GetFaces());
    foreach (Node child in node.GetChildren())
        CollectFaces(child, verts);
}
```

- [ ] **Step 2: Restructure CreateWorldObjectVisual — move collision inside each branch**

Replace the current `CreateWorldObjectVisual` body (lines 393–459). The key change is: delete the unconditional `shapeSize` switch and `CollisionShape3D` block at the end and instead add collision inside each branch:

```csharp
private Node3D CreateWorldObjectVisual(WorldObject obj)
{
	var body = new StaticBody3D { Name = $"WorldObject_{obj.Id}" };

	string? modelPath = obj.ObjectType switch
	{
		"tree_pine"  => "res://assets/models/nature/tree_pineRoundA.glb",
		"tree_dead"  => "res://assets/models/nature/tree_thin_dark.glb",
		"tree_palm"  => "res://assets/models/nature/tree_palmTall.glb",
		"rock_large" => "res://assets/models/nature/rock_largeA.glb",
		"rock_small" => "res://assets/models/nature/rock_smallA.glb",
		"bush"       => "res://assets/models/nature/plant_bush.glb",
		_            => null,
	};

	float modelScale = obj.ObjectType switch
	{
		"tree_pine" or "tree_palm" => 2.5f,
		"tree_dead"                => 2.0f,
		"rock_large"               => 2.0f,
		"rock_small"               => 1.8f,
		"bush"                     => 1.5f,
		_                          => 1.0f,
	};

	if (modelPath != null && ResourceLoader.Exists(modelPath))
	{
		var model = ResourceLoader.Load<PackedScene>(modelPath).Instantiate<Node3D>();
		model.Scale = Vector3.One * modelScale;
		body.AddChild(model);
		body.AddChild(new CollisionShape3D { Shape = BuildConvexShape(model, modelScale) });
	}
	else
	{
		body.AddChild(new MeshInstance3D
		{
			Mesh     = new BoxMesh { Size = new Vector3(0.8f, 1.5f, 0.8f) * modelScale },
			Position = new Vector3(0, 0.75f * modelScale, 0),
		});
		var shapeSize = obj.ObjectType switch
		{
			"tree_pine" or "tree_palm" => new Vector3(1.2f, 6.0f, 1.2f),
			"tree_dead"                => new Vector3(1.0f, 5.0f, 1.0f),
			"rock_large"               => new Vector3(2.4f, 1.6f, 2.4f),
			"rock_small"               => new Vector3(1.1f, 0.7f, 1.1f),
			"bush"                     => new Vector3(1.5f, 1.0f, 1.5f),
			_                          => new Vector3(0.8f, 1.0f, 0.8f),
		};
		body.AddChild(new CollisionShape3D
		{
			Shape    = new BoxShape3D { Size = shapeSize },
			Position = new Vector3(0, shapeSize.Y / 2f, 0),
		});
	}

	// Snap Y to client terrain height — ignores any server/client formula drift
	float groundY = Terrain.HeightAt(obj.PosX, obj.PosZ);
	body.Position = new Vector3(obj.PosX, groundY, obj.PosZ);
	body.Rotation = new Vector3(0, obj.RotY, 0);
	body.AddToGroup("world_object");
	body.SetMeta("world_object_id", (long)obj.Id);
	body.SetMeta("object_type", obj.ObjectType);

	return body;
}
```

- [ ] **Step 3: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "feat: convex hull collision for world objects from actual mesh geometry"
```

---

## Task 2: StructureModelPath Helper + Ghost Preview Fix

**Files:**
- Modify: `client/scripts/world/WorldManager.cs` — add `StructureModelPath` static helper
- Modify: `client/scripts/building/BuildSystem.cs` — update `CreateGhostPreview`; add `ApplyGhostMaterial` helper

- [ ] **Step 1: Add StructureModelPath to WorldManager**

Add the following public static method alongside `StructureFallbackMesh` and `StructureYOffset` (around line 233):

```csharp
public static string? StructureModelPath(string t) => t switch
{
	"wood_wall"   or "stone_wall"  => "res://assets/models/building/wall.glb",
	"wood_floor"  or "stone_floor" => "res://assets/models/building/floor.glb",
	"wood_door"                    => "res://assets/models/building/wall-doorway-square.glb",
	"campfire"                     => "res://assets/models/survival/campfire-pit.glb",
	"workbench"                    => "res://assets/models/survival/workbench.glb",
	"chest"                        => "res://assets/models/survival/chest.glb",
	_                              => null,
};
```

- [ ] **Step 2: Update CreateGhostPreview in BuildSystem.cs**

Note: `CreateStructureVisual` will be fully replaced in Task 5 with a version that already calls `StructureModelPath` — no intermediate edit needed here.

Replace the entire `CreateGhostPreview` method and add the `ApplyGhostMaterial` helper:

```csharp
private void CreateGhostPreview(string structureType)
{
	_ghostPreview = new Node3D { Name = "GhostPreview" };

	var modelPath = WorldManager.StructureModelPath(structureType);
	if (modelPath != null && ResourceLoader.Exists(modelPath))
	{
		var model = ResourceLoader.Load<PackedScene>(modelPath).Instantiate<Node3D>();
		ApplyGhostMaterial(model);
		_ghostPreview.AddChild(model);
	}
	else
	{
		var mesh = new MeshInstance3D
		{
			Mesh             = WorldManager.StructureFallbackMesh(structureType),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor  = new Color(0.3f, 0.8f, 0.3f, 0.4f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			},
		};
		mesh.Position = new Vector3(0, WorldManager.StructureYOffset(structureType), 0);
		_ghostPreview.AddChild(mesh);
	}

	GetParent().AddChild(_ghostPreview);
}

private static void ApplyGhostMaterial(Node node, StandardMaterial3D? mat = null)
{
	mat ??= new StandardMaterial3D
	{
		AlbedoColor  = new Color(0.3f, 0.8f, 0.3f, 0.4f),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	};
	if (node is MeshInstance3D mi) mi.MaterialOverride = mat;
	foreach (Node child in node.GetChildren())
		ApplyGhostMaterial(child, mat);
}
```

- [ ] **Step 3: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add client/scripts/world/WorldManager.cs client/scripts/building/BuildSystem.cs
git commit -m "fix: ghost preview uses real GLB model — fixes 90-degree placement rotation mismatch"
```

---

## Task 3: Item Spawn Height Fix

**Files:**
- Modify: `client/scripts/world/WorldManager.cs:198` — one line change

- [ ] **Step 1: Add +0.5f Y offset to item spawn position**

In `CreateWorldItemVisual`, change line 198:

```csharp
// Before:
body.Position = new Vector3(item.PosX, item.PosY, item.PosZ);

// After:
body.Position = new Vector3(item.PosX, item.PosY + 0.5f, item.PosZ);
```

- [ ] **Step 2: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "fix: spawn dropped items 0.5 units above terrain to prevent fall-through"
```

---

## Task 4: Water Coverage

**Files:**
- Modify: `client/scenes/Main.tscn` — two sub-resource values and the Ocean transform

- [ ] **Step 1: Update Ocean mesh size**

In `client/scenes/Main.tscn`, find:
```
[sub_resource type="PlaneMesh" id="PlaneMesh_ocean"]
size = Vector2(200, 120)
```

Change to:
```
[sub_resource type="PlaneMesh" id="PlaneMesh_ocean"]
size = Vector2(600, 252)
```

- [ ] **Step 2: Update Ocean transform**

In the same file, find:
```
[node name="Ocean" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -0.2, -30)
```

Change to:
```
[node name="Ocean" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, -124)
```

(Y: −0.2 → 0 = sea level. Z: −30 → −124 = centers coverage from z=−250 to z=+2. X size 600 = ±300 around world centre.)

- [ ] **Step 3: Commit**

```bash
git add client/scenes/Main.tscn
git commit -m "fix: extend water plane to cover full coastline — 600x252 at sea level"
```

---

## Task 5: Structure Collision + Wall Stacking

**Files:**
- Modify: `client/scripts/world/WorldManager.cs` — `_structures` field type; `CreateStructureVisual` return type and body

- [ ] **Step 1: Update _structures field type**

In `WorldManager.cs` at the top of the class (line ~16), change:

```csharp
// Before:
private readonly Dictionary<ulong, Node3D> _structures = new();

// After:
private readonly Dictionary<ulong, StaticBody3D> _structures = new();
```

- [ ] **Step 2: Replace CreateStructureVisual**

Replace the entire `CreateStructureVisual` method (lines 294–349) with:

```csharp
private StaticBody3D CreateStructureVisual(PlacedStructure structure)
{
	var body = new StaticBody3D { Name = $"Structure_{structure.Id}" };

	string? modelPath = StructureModelPath(structure.StructureType);

	if (modelPath != null && ResourceLoader.Exists(modelPath))
	{
		var scene  = ResourceLoader.Load<PackedScene>(modelPath);
		var visual = scene.Instantiate<Node3D>();
		Color? tint = structure.StructureType switch
		{
			"wood_wall" or "wood_floor" or "wood_door" => new Color(0.65f, 0.45f, 0.25f),
			"stone_wall" or "stone_floor"              => new Color(0.6f,  0.6f,  0.65f),
			_                                           => (Color?)null,
		};
		if (tint.HasValue) TintMeshes(visual, tint.Value);
		body.AddChild(visual);
	}
	else
	{
		bool isStone = structure.StructureType.Contains("stone");
		var mesh = new MeshInstance3D { Mesh = StructureFallbackMesh(structure.StructureType) };
		mesh.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = structure.StructureType switch
			{
				"campfire"     => new Color(0.8f, 0.3f,  0.1f),
				"workbench"    => new Color(0.5f, 0.35f, 0.2f),
				"chest"        => new Color(0.55f, 0.4f, 0.25f),
				_ when isStone => new Color(0.55f, 0.55f, 0.6f),
				_              => new Color(0.6f, 0.45f, 0.25f),
			},
			Roughness = 0.85f,
		};
		mesh.Position = new Vector3(0, StructureYOffset(structure.StructureType), 0);
		body.AddChild(mesh);
	}

	var (shapeSize, shapeCenter) = structure.StructureType switch
	{
		"wood_wall"   or "stone_wall"  => (new Vector3(2.5f, 2.5f, 0.25f), new Vector3(0, 1.25f, 0)),
		"wood_floor"  or "stone_floor" => (new Vector3(2.5f, 0.1f,  2.5f),  new Vector3(0, 0.05f, 0)),
		"wood_door"                    => (new Vector3(2.5f, 2.5f, 0.25f), new Vector3(0, 1.25f, 0)),
		"campfire"                     => (new Vector3(0.8f, 0.4f,  0.8f),  new Vector3(0, 0.2f,  0)),
		"workbench"                    => (new Vector3(1.2f, 0.8f,  0.6f),  new Vector3(0, 0.4f,  0)),
		"chest"                        => (new Vector3(0.8f, 0.6f,  0.6f),  new Vector3(0, 0.3f,  0)),
		_                              => (new Vector3(1.0f, 1.0f,  1.0f),  new Vector3(0, 0.5f,  0)),
	};
	body.AddChild(new CollisionShape3D
	{
		Shape    = new BoxShape3D { Size = shapeSize },
		Position = shapeCenter,
	});

	body.Position = new Vector3(structure.PosX, structure.PosY, structure.PosZ);
	body.Rotation = new Vector3(0, structure.RotY, 0);
	body.SetMeta("structure_id",  (long)structure.Id);
	body.SetMeta("structure_type", structure.StructureType);
	body.SetMeta("owner_id",       structure.OwnerId.ToString());

	return body;
}
```

- [ ] **Step 3: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "feat: placed structures get StaticBody3D collision — enables wall stacking + solid walls/floors"
```

---

## Task 6: In-Game Verification

- [ ] **Step 1: Start server (no server changes, but fresh state helps)**

Use the `start_server` MCP tool.

- [ ] **Step 2: Launch game**

Use the `start_godot` MCP tool (`editor=false`).

- [ ] **Step 3: Verify**

- [ ] Hitboxes: harvest a tree — raycasts should feel tight around the trunk, not a wide box
- [ ] Rotation: place a wall — the ghost preview should show the same orientation as the placed wall (no 90° mismatch)
- [ ] Items: harvest a tree; dropped wood should land on terrain without falling through
- [ ] Water: the ocean meets the beach smoothly; no visible water edge in the play area
- [ ] Stacking: place a wall; aim at the top of the placed wall; a second wall should snap on top
