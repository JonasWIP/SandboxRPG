using Godot;

namespace SandboxRPG;

/// <summary>
/// Building system — no separate build mode toggle.
/// Building is active automatically when the selected hotbar slot holds a
/// placeable structure item.  Left-click places, R rotates the ghost.
/// </summary>
public partial class BuildSystem : Node
{
	[Export] public float GridSize   = 1.0f;
	[Export] public float PlaceRange = 10.0f;

	private static readonly System.Collections.Generic.HashSet<string> BuildableTypes = new()
	{
		"wood_wall", "stone_wall", "wood_floor", "stone_floor",
		"wood_door", "campfire", "workbench", "chest",
	};

	private Node3D?  _ghostPreview;
	private Camera3D? _camera;
	private string?   _currentGhostType; // item type the ghost was built for

	public override void _Process(double delta)
	{
		_camera ??= GetViewport()?.GetCamera3D();

		// Suppress building while any UI panel is open
		if (UIManager.Instance.IsAnyOpen)
		{
			ClearGhost();
			return;
		}

		var activeItem = Hotbar.Instance?.ActiveItemType;
		bool isBuildable = activeItem != null && BuildableTypes.Contains(activeItem);

		if (!isBuildable)
		{
			ClearGhost();
			return;
		}

		// Rebuild ghost if the active item changed
		if (activeItem != _currentGhostType)
		{
			ClearGhost();
			_currentGhostType = activeItem;
		}

		UpdateGhostPosition();

		// Rotate ghost with R
		if (Input.IsKeyPressed(Key.R) && _ghostPreview != null)
			_ghostPreview.RotateY(Mathf.Pi / 2 * (float)delta * 3);

		// Place on left-click
		if (Input.IsMouseButtonPressed(MouseButton.Left) && _ghostPreview != null)
			PlaceStructure();
	}

	// =========================================================================
	// GHOST
	// =========================================================================

	private void UpdateGhostPosition()
	{
		if (_camera == null) return;

		var screenCenter = GetViewport().GetVisibleRect().Size / 2;
		var from      = _camera.ProjectRayOrigin(screenCenter);
		var direction = _camera.ProjectRayNormal(screenCenter);

		if (direction.Y == 0) return;

		float t = -from.Y / direction.Y;
		if (t <= 0 || t >= PlaceRange) return;

		var hit = from + direction * t;
		float snappedX = Mathf.Round(hit.X / GridSize) * GridSize;
		float snappedZ = Mathf.Round(hit.Z / GridSize) * GridSize;

		if (_ghostPreview == null)
			CreateGhostPreview(_currentGhostType!);

		if (_ghostPreview != null)
			_ghostPreview.GlobalPosition = new Vector3(snappedX, 0, snappedZ);
	}

	private void CreateGhostPreview(string structureType)
	{
		_ghostPreview = new Node3D { Name = "GhostPreview" };

		var mesh = new MeshInstance3D();
		mesh.Mesh = structureType switch
		{
			"wood_wall"  or "stone_wall"  => new BoxMesh { Size = new Vector3(2f, 2.5f, 0.2f) },
			"wood_floor" or "stone_floor" => new BoxMesh { Size = new Vector3(2f, 0.1f, 2f) },
			"wood_door"                   => new BoxMesh { Size = new Vector3(1f, 2.2f, 0.15f) },
			"campfire"                    => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 0.3f },
			"workbench"                   => new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) },
			"chest"                       => new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.5f) },
			_                             => new BoxMesh { Size = new Vector3(1f, 1f, 1f) },
		};

		mesh.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor  = new Color(0.3f, 0.8f, 0.3f, 0.4f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		mesh.Position = new Vector3(0, structureType switch
		{
			"wood_wall"  or "stone_wall"  => 1.25f,
			"wood_floor" or "stone_floor" => 0.05f,
			"wood_door"                   => 1.1f,
			"campfire"                    => 0.15f,
			"workbench"                   => 0.4f,
			"chest"                       => 0.3f,
			_                             => 0.5f,
		}, 0);

		_ghostPreview.AddChild(mesh);
		GetParent().AddChild(_ghostPreview);
	}

	private void ClearGhost()
	{
		if (_ghostPreview == null) return;
		_ghostPreview.QueueFree();
		_ghostPreview    = null;
		_currentGhostType = null;
	}

	// =========================================================================
	// PLACE
	// =========================================================================

	private void PlaceStructure()
	{
		if (_ghostPreview == null || _currentGhostType == null) return;

		var pos  = _ghostPreview.GlobalPosition;
		var rotY = _ghostPreview.Rotation.Y;
		GameManager.Instance.PlaceBuildStructure(_currentGhostType, pos.X, pos.Y, pos.Z, rotY);
	}
}
