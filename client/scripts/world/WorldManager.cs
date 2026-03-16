using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG;

/// <summary>
/// Manages the 3D world: spawns/removes players, world items, structures,
/// and server-authoritative world objects (trees, rocks, bushes).
/// </summary>
public partial class WorldManager : Node3D
{
	// === Tracked entities ===
	private readonly Dictionary<string, RemotePlayer> _remotePlayers = new();
	private readonly Dictionary<ulong, Node3D> _worldItems = new();
	private readonly Dictionary<ulong, StaticBody3D> _structures = new();
	private readonly Dictionary<ulong, Node3D> _worldObjects = new();

	private PlayerController? _localPlayer;
	private bool _worldSpawned;

	public override void _Ready()
	{
		var gm = GameManager.Instance;
		gm.SubscriptionApplied += OnSubscriptionApplied;
		gm.PlayerUpdated += OnPlayerUpdated;
		gm.PlayerRemoved += OnPlayerRemoved;
		gm.WorldItemChanged += OnWorldItemsChanged;
		gm.StructureChanged += OnStructuresChanged;
		gm.WorldObjectUpdated += OnWorldObjectUpdated;

		if (GameManager.Instance.IsConnected && GameManager.Instance.GetLocalPlayer() != null)
			OnSubscriptionApplied();
	}

	private void OnSubscriptionApplied()
	{
		if (_worldSpawned) return;
		_worldSpawned = true;
		GD.Print("[WorldManager] Initial data received, spawning world...");

		SpawnLocalPlayer();

		foreach (var player in GameManager.Instance.GetAllPlayers())
		{
			if (player.Identity != GameManager.Instance.LocalIdentity && player.IsOnline)
				SpawnOrUpdateRemotePlayer(player);
		}

		OnWorldItemsChanged();
		OnStructuresChanged();
		OnWorldObjectsChanged();
	}

	// =========================================================================
	// PLAYER MANAGEMENT
	// =========================================================================

	private void SpawnLocalPlayer()
	{
		var playerData = GameManager.Instance.GetLocalPlayer();
		if (playerData == null) return;

		var p = playerData;
		_localPlayer = new PlayerController { Name = "LocalPlayer" };
		AddChild(_localPlayer);
		_localPlayer.GlobalPosition = new Vector3(p.PosX, p.PosY, p.PosZ);
		_localPlayer.Rotation = new Vector3(0, p.RotY, 0);
		_localPlayer.ApplyColor(p.ColorHex ?? PlayerPrefs.LoadColorHex());

		GD.Print($"[WorldManager] Local player spawned at ({p.PosX}, {p.PosY}, {p.PosZ})");
	}

	private void OnPlayerUpdated(string identityHex)
	{
		foreach (var player in GameManager.Instance.GetAllPlayers())
		{
			if (player.Identity.ToString() == identityHex)
			{
				if (player.Identity == GameManager.Instance.LocalIdentity)
				{
					_localPlayer?.ApplyColor(player.ColorHex ?? PlayerPrefs.LoadColorHex());
					return;
				}

				if (player.IsOnline)
					SpawnOrUpdateRemotePlayer(player);
				else
					RemoveRemotePlayer(identityHex);
				break;
			}
		}
	}

	private void SpawnOrUpdateRemotePlayer(Player player)
	{
		string id = player.Identity.ToString();
		string colorHex = player.ColorHex ?? "#E6804D";
		if (_remotePlayers.TryGetValue(id, out var existing))
		{
			existing.UpdateFromServer(player.PosX, player.PosY, player.PosZ, player.RotY, player.Name, colorHex);
		}
		else
		{
			var remote = new RemotePlayer
			{
				Name = $"Remote_{id[..8]}",
				IdentityHex = id,
				PlayerName = player.Name,
				ColorHex = colorHex,
			};
			AddChild(remote);
			remote.GlobalPosition = new Vector3(player.PosX, player.PosY, player.PosZ);
			remote.Rotation = new Vector3(0, player.RotY, 0);
			_remotePlayers[id] = remote;
			GD.Print($"[WorldManager] Remote player spawned: {player.Name}");
		}
	}

	private void OnPlayerRemoved(string identityHex) => RemoveRemotePlayer(identityHex);

	private void RemoveRemotePlayer(string identityHex)
	{
		if (_remotePlayers.TryGetValue(identityHex, out var remote))
		{
			remote.QueueFree();
			_remotePlayers.Remove(identityHex);
		}
	}

	// =========================================================================
	// WORLD ITEMS
	// =========================================================================

	private void OnWorldItemsChanged()
	{
		var currentIds = new HashSet<ulong>();

		foreach (var item in GameManager.Instance.GetAllWorldItems())
		{
			currentIds.Add(item.Id);
			if (!_worldItems.ContainsKey(item.Id))
			{
				var visual = CreateWorldItemVisual(item);
				AddChild(visual);
				_worldItems[item.Id] = visual;
			}
		}

		var toRemove = new List<ulong>();
		foreach (var kvp in _worldItems)
			if (!currentIds.Contains(kvp.Key))
			{
				kvp.Value.QueueFree();
				toRemove.Add(kvp.Key);
			}
		foreach (var id in toRemove)
			_worldItems.Remove(id);
	}

	private Node3D CreateWorldItemVisual(WorldItem item)
	{
		var body = new StaticBody3D { Name = $"WorldItem_{item.Id}", CollisionLayer = 2, CollisionMask = 0 };

		var modelPath = WorldItemModelPath(item.ItemType);
		if (modelPath != null && ResourceLoader.Exists(modelPath))
		{
			var model = ResourceLoader.Load<PackedScene>(modelPath).Instantiate<Node3D>();
			model.Position = new Vector3(0, 0.1f, 0);
			FixMaterials(model);
			body.AddChild(model);
		}
		else
		{
			body.AddChild(CreateFallbackItemMesh(item.ItemType));
		}

		body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.2f } });

		body.AddChild(new Label3D
		{
			Text        = $"{item.ItemType} x{item.Quantity}",
			FontSize    = 32,
			Billboard   = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			Position    = new Vector3(0, 0.5f, 0),
		});

		float groundY = Terrain.HeightAt(item.PosX, item.PosZ);
		body.Position = new Vector3(item.PosX, groundY + 0.1f, item.PosZ);
		body.SetMeta("world_item_id", (long)item.Id);
		body.SetMeta("item_type", item.ItemType);
		return body;
	}

	private static string? WorldItemModelPath(string itemType) => itemType switch
	{
		"wood"   => "res://assets/models/survival/resource-wood.glb",
		"stone"  => "res://assets/models/survival/resource-stone.glb",
		"planks" => "res://assets/models/survival/resource-planks.glb",
		_        => null,
	};

	private static MeshInstance3D CreateFallbackItemMesh(string itemType)
	{
		var mesh = new MeshInstance3D
		{
			Mesh     = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.4f) },
			Position = new Vector3(0, 0.2f, 0),
		};
		mesh.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = itemType switch
			{
				"wood"  => new Color(0.6f, 0.4f, 0.2f),
				"stone" => new Color(0.5f, 0.5f, 0.55f),
				"iron"  => new Color(0.7f, 0.7f, 0.75f),
				_       => new Color(0.8f, 0.8f, 0.2f),
			},
			Roughness = 0.9f,
		};
		return mesh;
	}

	public static Mesh StructureFallbackMesh(string t) => t switch
	{
		"wood_wall" or "stone_wall"   => new BoxMesh { Size = new Vector3(2f, 2.5f, 0.2f) },
		"wood_floor" or "stone_floor" => new BoxMesh { Size = new Vector3(2f, 0.1f, 2f) },
		"wood_door"                   => new BoxMesh { Size = new Vector3(1f, 2.2f, 0.15f) },
		"campfire"                    => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 0.3f },
		"workbench"                   => new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) },
		"chest"                       => new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.5f) },
		_                             => new BoxMesh { Size = new Vector3(1f, 1f, 1f) },
	};

	public static float StructureYOffset(string t) => t switch
	{
		"wood_wall" or "stone_wall"   => 1.25f,
		"wood_floor" or "stone_floor" => 0.05f,
		"wood_door"                   => 1.1f,
		"campfire"                    => 0.15f,
		"workbench"                   => 0.4f,
		"chest"                       => 0.3f,
		_                             => 0.5f,
	};

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

	/// <summary>Recursively tints MeshInstance3D nodes, duplicating existing materials so textures are preserved.</summary>
	private static void TintMeshes(Node root, Color color)
	{
		if (root is MeshInstance3D mi && mi.Mesh != null)
		{
			for (int surf = 0; surf < mi.Mesh.GetSurfaceCount(); surf++)
			{
				var existing = mi.GetActiveMaterial(surf);
				if (existing is BaseMaterial3D baseMat)
				{
					var dup = (BaseMaterial3D)baseMat.Duplicate();
					dup.AlbedoColor = color;
					mi.SetSurfaceOverrideMaterial(surf, dup);
				}
				else
				{
					mi.SetSurfaceOverrideMaterial(surf, new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f });
				}
			}
		}
		foreach (Node child in root.GetChildren())
			TintMeshes(child, color);
	}

	private static (Vector3 size, Vector3 center) GetStructureBoxShape(string t) => t switch
	{
		"wood_wall"   or "stone_wall"  => (new Vector3(0.25f, 2.4f, 2.0f), new Vector3(0, 1.2f, 0)),
		"wood_floor"  or "stone_floor" => (new Vector3(2.0f,  0.1f, 2.0f), new Vector3(0, 0.05f, 0)),
		"wood_door"                    => (new Vector3(0.25f, 2.4f, 2.0f), new Vector3(0, 1.2f, 0)),
		"campfire"                     => (new Vector3(0.8f, 0.4f,  0.8f),  new Vector3(0, 0.2f,  0)),
		"workbench"                    => (new Vector3(1.2f, 0.8f,  0.6f),  new Vector3(0, 0.4f,  0)),
		"chest"                        => (new Vector3(0.8f, 0.6f,  0.6f),  new Vector3(0, 0.3f,  0)),
		_                              => (new Vector3(1.0f, 1.0f,  1.0f),  new Vector3(0, 0.5f,  0)),
	};

	// =========================================================================
	// STRUCTURES
	// =========================================================================

	private void OnStructuresChanged()
	{
		var currentIds = new HashSet<ulong>();

		foreach (var structure in GameManager.Instance.GetAllStructures())
		{
			currentIds.Add(structure.Id);
			if (!_structures.ContainsKey(structure.Id))
			{
				var visual = CreateStructureVisual(structure);
				AddChild(visual);
				_structures[structure.Id] = visual;
			}
		}

		var toRemove = new List<ulong>();
		foreach (var kvp in _structures)
			if (!currentIds.Contains(kvp.Key))
			{
				kvp.Value.QueueFree();
				toRemove.Add(kvp.Key);
			}
		foreach (var id in toRemove)
			_structures.Remove(id);
	}

	private StaticBody3D CreateStructureVisual(PlacedStructure structure)
	{
		var body = new StaticBody3D { Name = $"Structure_{structure.Id}", CollisionLayer = 1, CollisionMask = 1 };

		string? modelPath = StructureModelPath(structure.StructureType);

		if (modelPath != null && ResourceLoader.Exists(modelPath))
		{
			var scene  = ResourceLoader.Load<PackedScene>(modelPath);
			var visual = scene.Instantiate<Node3D>();
			Color? tint = structure.StructureType switch
			{
				"wood_wall" or "wood_floor" or "wood_door" => new Color(1.0f, 0.78f, 0.55f),
				"stone_wall" or "stone_floor"              => new Color(0.82f, 0.82f, 0.88f),
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

		var (sz, sc) = GetStructureBoxShape(structure.StructureType);
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = sz }, Position = sc });
		body.Position = new Vector3(structure.PosX, structure.PosY, structure.PosZ);
		body.Rotation = new Vector3(0, structure.RotY, 0);
		body.SetMeta("structure_id",  (long)structure.Id);
		body.SetMeta("structure_type", structure.StructureType);
		body.SetMeta("owner_id",       structure.OwnerId.ToString());

		return body;
	}

	// =========================================================================
	// WORLD OBJECTS (trees, rocks, bushes — server-authoritative)
	// =========================================================================

	private void OnWorldObjectsChanged()
	{
		foreach (var obj in GameManager.Instance.GetAllWorldObjects())
		{
			if (!_worldObjects.ContainsKey(obj.Id))
			{
				var visual = CreateWorldObjectVisual(obj);
				AddChild(visual);
				_worldObjects[obj.Id] = visual;
			}
		}
	}

	private void OnWorldObjectUpdated(long id, bool removed)
	{
		ulong uid = (ulong)id;
		if (removed)
		{
			if (_worldObjects.TryGetValue(uid, out var existing))
			{
				existing.QueueFree();
				_worldObjects.Remove(uid);
			}
			return;
		}

		// New insert (from delete+reinsert on damage) — add if not already present
		var obj = GameManager.Instance.GetWorldObject(uid);
		if (obj is null) return;

		if (!_worldObjects.ContainsKey(uid))
		{
			var visual = CreateWorldObjectVisual(obj);
			AddChild(visual);
			_worldObjects[uid] = visual;
		}
	}

	/// <summary>Duplicates surface materials, zeroes metallic (enables diffuse lighting), and dims brightness.</summary>
	private static void FixMaterials(Node root, float brightness = 0.85f)
	{
		if (root is MeshInstance3D mi && mi.Mesh != null)
		{
			for (int surf = 0; surf < mi.Mesh.GetSurfaceCount(); surf++)
			{
				var mat = mi.GetActiveMaterial(surf);
				if (mat is not BaseMaterial3D bm) continue;
				var dup = (BaseMaterial3D)bm.Duplicate();
				dup.Metallic = 0f;
				var c = dup.AlbedoColor;
				dup.AlbedoColor = new Color(c.R * brightness, c.G * brightness, c.B * brightness, c.A);
				mi.SetSurfaceOverrideMaterial(surf, dup);
			}
		}
		foreach (Node child in root.GetChildren())
			FixMaterials(child, brightness);
	}

	private static ConvexPolygonShape3D BuildConvexShape(Node3D model, float scale)
	{
		var pts = new List<Vector3>();
		foreach (var mi in model.FindChildren("*", "MeshInstance3D", owned: false).OfType<MeshInstance3D>())
		{
			if (mi.Mesh is not ArrayMesh arrayMesh) continue;
			var shape = arrayMesh.CreateConvexShape(clean: true, simplify: false);
			pts.AddRange(shape.Points);
		}
		for (int i = 0; i < pts.Count; i++)
			pts[i] *= scale;
		return new ConvexPolygonShape3D { Points = pts.ToArray() };
	}

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
			FixMaterials(model);
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
}
