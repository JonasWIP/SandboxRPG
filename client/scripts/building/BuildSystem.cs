using Godot;

namespace SandboxRPG;

/// <summary>
/// Building system — no separate build mode toggle.
/// Building is active automatically when the selected hotbar slot holds a
/// placeable structure item.  Left-click places, R rotates the ghost 90° steps.
/// Ghost uses Y-axis rotation only (no slope alignment).
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

	public static bool IsBuildable(string? itemType) =>
		itemType != null && BuildableTypes.Contains(itemType);

	private Node3D?   _ghostPreview;
	private Camera3D? _camera;
	private string?   _currentGhostType;
	private float _ghostRotationY = 0f;  // accumulated rotation in degrees (90° steps)
	private bool  _rWasPressed    = false;

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
		bool isBuildable = IsBuildable(activeItem);

		if (!isBuildable)
		{
			ClearGhost();
			return;
		}

		// Rebuild ghost if the active item changed
		if (activeItem != _currentGhostType)
		{
			ClearGhost();
			_ghostRotationY = 0f;
			_currentGhostType = activeItem;
		}

		UpdateGhostPosition();

		// Discrete 90° step on R press
		if (Input.IsKeyPressed(Key.R) && !_rWasPressed)
			_ghostRotationY = (_ghostRotationY + 90f) % 360f;
		_rWasPressed = Input.IsKeyPressed(Key.R);

		// Place on left-click
		if (Input.IsActionJustPressed("primary_attack") && _ghostPreview != null)
			PlaceStructure();
	}

	// =========================================================================
	// GHOST
	// =========================================================================

	private void UpdateGhostPosition()
	{
		if (_camera == null) return;
		var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
		if (spaceState == null) return;

		var screenCenter = GetViewport().GetVisibleRect().Size / 2;
		var from = _camera.ProjectRayOrigin(screenCenter);
		var dir  = _camera.ProjectRayNormal(screenCenter);

		var result = spaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + dir * PlaceRange));
		if (result.Count == 0) return;

		var hitPos = (Vector3)result["position"];
		hitPos.X = Mathf.Round(hitPos.X / GridSize) * GridSize;
		hitPos.Z = Mathf.Round(hitPos.Z / GridSize) * GridSize;

		if (_ghostPreview == null) CreateGhostPreview(_currentGhostType!);
		if (_ghostPreview == null) return;

		var yRot = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(_ghostRotationY), 0));
		_ghostPreview.GlobalTransform = new Transform3D(yRot, hitPos);
	}

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
		var rotY = Mathf.DegToRad(_ghostRotationY);
		GameManager.Instance.PlaceBuildStructure(_currentGhostType, pos.X, pos.Y, pos.Z, rotY);
	}
}
