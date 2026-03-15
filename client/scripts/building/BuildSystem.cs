using Godot;

namespace SandboxRPG;

/// <summary>
/// Building system — no separate build mode toggle.
/// Building is active automatically when the selected hotbar slot holds a
/// placeable structure item.  Left-click places, R rotates the ghost 90° steps.
/// Ghost aligns to terrain surface normal for slope-aware placement.
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
			_ghostRotationY = 0f;
			_currentGhostType = activeItem;
		}

		UpdateGhostPosition();

		// Discrete 90° step on R press
		if (Input.IsKeyPressed(Key.R) && !_rWasPressed)
			_ghostRotationY = (_ghostRotationY + 90f) % 360f;
		_rWasPressed = Input.IsKeyPressed(Key.R);

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

		var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
		if (spaceState == null) return;

		var screenCenter = GetViewport().GetVisibleRect().Size / 2;
		var from = _camera.ProjectRayOrigin(screenCenter);
		var dir  = _camera.ProjectRayNormal(screenCenter);

		var query = PhysicsRayQueryParameters3D.Create(from, from + dir * PlaceRange);
		var result = spaceState.IntersectRay(query);
		if (result.Count == 0) return;

		var hitPos = (Vector3)result["position"];
		var normal = (Vector3)result["normal"];

		// Grid snap X/Z; Y from terrain surface hit
		hitPos.X = Mathf.Round(hitPos.X / GridSize) * GridSize;
		hitPos.Z = Mathf.Round(hitPos.Z / GridSize) * GridSize;

		if (_ghostPreview == null)
			CreateGhostPreview(_currentGhostType!);

		if (_ghostPreview == null) return;

		// Align ghost up-axis to terrain normal, then apply player Y rotation on top.
		// Use Vector3.Right as fallback when normal is parallel to Vector3.Forward
		// (avoids zero cross-product on vertical surfaces).
		var up    = normal.Normalized();
		var right = up.Cross(Vector3.Forward);
		if (right.LengthSquared() < 0.001f)
			right = up.Cross(Vector3.Right);
		right = right.Normalized();
		var forward      = right.Cross(up).Normalized();
		var surfaceBasis = new Basis(right, up, -forward);
		var yRotBasis    = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(_ghostRotationY), 0));

		_ghostPreview.GlobalTransform = new Transform3D(surfaceBasis * yRotBasis, hitPos);
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
		var rotY = Mathf.DegToRad(_ghostRotationY);
		GameManager.Instance.PlaceBuildStructure(_currentGhostType, pos.X, pos.Y, pos.Z, rotY);
	}
}
