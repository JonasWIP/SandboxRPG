using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Manages the 3D world: spawns/removes players, world items, and structures
/// based on SpacetimeDB state changes.
/// </summary>
public partial class WorldManager : Node3D
{
	// === Tracked entities ===
	private readonly Dictionary<string, RemotePlayer> _remotePlayers = new();
	private readonly Dictionary<ulong, Node3D> _worldItems = new();
	private readonly Dictionary<ulong, Node3D> _structures = new();

	private PlayerController? _localPlayer;

	public override void _Ready()
	{
		// Connect to GameManager signals
		var gm = GameManager.Instance;
		gm.SubscriptionApplied += OnSubscriptionApplied;
		gm.PlayerUpdated += OnPlayerUpdated;
		gm.PlayerRemoved += OnPlayerRemoved;
		gm.WorldItemChanged += OnWorldItemsChanged;
		gm.StructureChanged += OnStructuresChanged;

		// If the subscription was applied before this scene loaded (the normal
		// flow after MainMenu → CharacterSetup → Game), spawn the world now.
		if (GameManager.Instance.IsConnected && GameManager.Instance.GetLocalPlayer() != null)
			OnSubscriptionApplied();
	}

	private void OnSubscriptionApplied()
	{
		GD.Print("[WorldManager] Initial data received, spawning world...");

		// Spawn local player
		SpawnLocalPlayer();

		// Spawn all currently online remote players
		foreach (var player in GameManager.Instance.GetAllPlayers())
		{
			if (player.Identity != GameManager.Instance.LocalIdentity && player.IsOnline)
				SpawnOrUpdateRemotePlayer(player);
		}

		// Spawn world items
		OnWorldItemsChanged();

		// Spawn structures
		OnStructuresChanged();
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
		// Find the player data
		foreach (var player in GameManager.Instance.GetAllPlayers())
		{
			if (player.Identity.ToString() == identityHex)
			{
				if (player.Identity == GameManager.Instance.LocalIdentity)
				{
					// Apply color updates to local player
					_localPlayer?.ApplyColor(player.ColorHex ?? PlayerPrefs.LoadColorHex());
					return;
				}

				if (player.IsOnline)
				{
					SpawnOrUpdateRemotePlayer(player);
				}
				else
				{
					RemoveRemotePlayer(identityHex);
				}
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

	private void OnPlayerRemoved(string identityHex)
	{
		RemoveRemotePlayer(identityHex);
	}

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
		// Simple approach: rebuild all world item visuals
		// For production, use differential updates
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

		// Remove items no longer in the database
		var toRemove = new List<ulong>();
		foreach (var kvp in _worldItems)
		{
			if (!currentIds.Contains(kvp.Key))
			{
				kvp.Value.QueueFree();
				toRemove.Add(kvp.Key);
			}
		}
		foreach (var id in toRemove)
			_worldItems.Remove(id);
	}

	private Node3D CreateWorldItemVisual(WorldItem item)
	{
		var node = new Node3D { Name = $"WorldItem_{item.Id}" };

		var mesh = new MeshInstance3D();
		var box = new BoxMesh
		{
			Size = new Vector3(0.4f, 0.4f, 0.4f),
		};
		mesh.Mesh = box;
		mesh.Position = new Vector3(0, 0.2f, 0);

		// Color based on item type
		var material = new StandardMaterial3D
		{
			AlbedoColor = item.ItemType switch
			{
				"wood" => new Color(0.6f, 0.4f, 0.2f),
				"stone" => new Color(0.5f, 0.5f, 0.55f),
				"iron" => new Color(0.7f, 0.7f, 0.75f),
				_ => new Color(0.8f, 0.8f, 0.2f),
			},
			Roughness = 0.9f,
		};
		mesh.MaterialOverride = material;
		node.AddChild(mesh);

		// Floating label
		var label = new Label3D
		{
			Text = $"{item.ItemType} x{item.Quantity}",
			FontSize = 32,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			Position = new Vector3(0, 0.7f, 0),
		};
		node.AddChild(label);

		node.Position = new Vector3(item.PosX, item.PosY, item.PosZ);

		// Store item ID as metadata for interaction
		node.SetMeta("world_item_id", (long)item.Id);
		node.SetMeta("item_type", item.ItemType);

		return node;
	}

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
		{
			if (!currentIds.Contains(kvp.Key))
			{
				kvp.Value.QueueFree();
				toRemove.Add(kvp.Key);
			}
		}
		foreach (var id in toRemove)
			_structures.Remove(id);
	}

	private Node3D CreateStructureVisual(PlacedStructure structure)
	{
		var node = new Node3D { Name = $"Structure_{structure.Id}" };

		var mesh = new MeshInstance3D();

		// Different meshes based on structure type
		Mesh structureMesh = structure.StructureType switch
		{
			"wood_wall" or "stone_wall" => new BoxMesh { Size = new Vector3(2f, 2.5f, 0.2f) },
			"wood_floor" or "stone_floor" => new BoxMesh { Size = new Vector3(2f, 0.1f, 2f) },
			"wood_door" => new BoxMesh { Size = new Vector3(1f, 2.2f, 0.15f) },
			"campfire" => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 0.3f },
			"workbench" => new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) },
			"chest" => new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.5f) },
			_ => new BoxMesh { Size = new Vector3(1f, 1f, 1f) },
		};
		mesh.Mesh = structureMesh;

		// Material based on type
		bool isStone = structure.StructureType.Contains("stone");
		var material = new StandardMaterial3D
		{
			AlbedoColor = structure.StructureType switch
			{
				"campfire" => new Color(0.8f, 0.3f, 0.1f),
				"workbench" => new Color(0.5f, 0.35f, 0.2f),
				"chest" => new Color(0.55f, 0.4f, 0.25f),
				_ when isStone => new Color(0.55f, 0.55f, 0.6f),
				_ => new Color(0.6f, 0.45f, 0.25f), // wood default
			},
			Roughness = 0.85f,
		};
		mesh.MaterialOverride = material;

		// Position mesh center
		float yOffset = structure.StructureType switch
		{
			"wood_wall" or "stone_wall" => 1.25f,
			"wood_floor" or "stone_floor" => 0.05f,
			"wood_door" => 1.1f,
			"campfire" => 0.15f,
			"workbench" => 0.4f,
			"chest" => 0.3f,
			_ => 0.5f,
		};
		mesh.Position = new Vector3(0, yOffset, 0);
		node.AddChild(mesh);

		node.Position = new Vector3(structure.PosX, structure.PosY, structure.PosZ);
		node.Rotation = new Vector3(0, structure.RotY, 0);

		// Store structure ID
		node.SetMeta("structure_id", (long)structure.Id);
		node.SetMeta("structure_type", structure.StructureType);
		node.SetMeta("owner_id", structure.OwnerId.ToString());

		return node;
	}
}
