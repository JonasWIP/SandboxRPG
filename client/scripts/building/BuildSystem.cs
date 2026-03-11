using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Building system: allows players to place and remove structures.
/// Shows ghost preview before placing, snaps to grid.
/// </summary>
public partial class BuildSystem : Node
{
	[Export] public float GridSize = 1.0f;
	[Export] public float PlaceRange = 10.0f;

	private bool _buildMode;
	private string _selectedStructure = "wood_wall";
	private Node3D? _ghostPreview;
	private Camera3D? _camera;
	private Label? _buildModeLabel;

	// Available buildable items (must be in inventory)
	private readonly string[] _buildableTypes = {
		"wood_wall", "stone_wall", "wood_floor", "stone_floor",
		"wood_door", "campfire", "workbench", "chest"
	};
	private int _selectedIndex;

	public override void _Ready()
	{
		_buildModeLabel = new Label
		{
			Text = "[B] Build Mode",
			AnchorLeft = 0,
			AnchorTop = 0,
			OffsetLeft = 10,
			OffsetTop = 10,
			Visible = true,
		};
		_buildModeLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
		AddChild(_buildModeLabel);
	}

	public override void _Process(double delta)
	{
		_camera ??= GetViewport()?.GetCamera3D();

		// Toggle build mode
		if (Input.IsActionJustPressed("build_mode"))
		{
			_buildMode = !_buildMode;
			UpdateBuildModeUI();

			if (!_buildMode && _ghostPreview != null)
			{
				_ghostPreview.QueueFree();
				_ghostPreview = null;
			}
		}

		if (!_buildMode) return;

		// Scroll to change selected structure
		if (Input.IsActionJustPressed("ui_page_up") || Input.IsKeyPressed(Key.Bracketright))
		{
			_selectedIndex = (_selectedIndex + 1) % _buildableTypes.Length;
			_selectedStructure = _buildableTypes[_selectedIndex];
			UpdateBuildModeUI();
			RecreateGhost();
		}
		if (Input.IsActionJustPressed("ui_page_down") || Input.IsKeyPressed(Key.Bracketleft))
		{
			_selectedIndex = (_selectedIndex - 1 + _buildableTypes.Length) % _buildableTypes.Length;
			_selectedStructure = _buildableTypes[_selectedIndex];
			UpdateBuildModeUI();
			RecreateGhost();
		}

		// Update ghost position
		UpdateGhostPosition();

		// Place structure on click
		if (Input.IsMouseButtonPressed(MouseButton.Left) && _ghostPreview != null)
		{
			PlaceStructure();
		}

		// Rotate ghost
		if (Input.IsKeyPressed(Key.R))
		{
			if (_ghostPreview != null)
			{
				_ghostPreview.RotateY(Mathf.Pi / 2 * (float)delta * 3);
			}
		}
	}

	private void UpdateGhostPosition()
	{
		if (_camera == null) return;

		// Raycast to find placement point
		var screenCenter = GetViewport().GetVisibleRect().Size / 2;
		var from = _camera.ProjectRayOrigin(screenCenter);
		var direction = _camera.ProjectRayNormal(screenCenter);

		// Simple ground plane intersection at y=0
		if (direction.Y != 0)
		{
			float t = -from.Y / direction.Y;
			if (t > 0 && t < PlaceRange)
			{
				var hitPoint = from + direction * t;

				// Snap to grid
				float snappedX = Mathf.Round(hitPoint.X / GridSize) * GridSize;
				float snappedZ = Mathf.Round(hitPoint.Z / GridSize) * GridSize;

				if (_ghostPreview == null)
				{
					CreateGhostPreview();
				}

				if (_ghostPreview != null)
				{
					_ghostPreview.GlobalPosition = new Vector3(snappedX, 0, snappedZ);
				}
			}
		}
	}

	private void CreateGhostPreview()
	{
		_ghostPreview = new Node3D { Name = "GhostPreview" };

		var mesh = new MeshInstance3D();
		mesh.Mesh = _selectedStructure switch
		{
			"wood_wall" or "stone_wall" => new BoxMesh { Size = new Vector3(2f, 2.5f, 0.2f) },
			"wood_floor" or "stone_floor" => new BoxMesh { Size = new Vector3(2f, 0.1f, 2f) },
			"wood_door" => new BoxMesh { Size = new Vector3(1f, 2.2f, 0.15f) },
			"campfire" => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 0.3f },
			"workbench" => new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) },
			"chest" => new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.5f) },
			_ => new BoxMesh { Size = new Vector3(1f, 1f, 1f) },
		};

		// Ghost material (semi-transparent)
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.3f, 0.8f, 0.3f, 0.4f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		mesh.MaterialOverride = material;

		float yOffset = _selectedStructure switch
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

		_ghostPreview.AddChild(mesh);
		GetParent().AddChild(_ghostPreview);
	}

	private void RecreateGhost()
	{
		if (_ghostPreview != null)
		{
			var pos = _ghostPreview.GlobalPosition;
			var rot = _ghostPreview.Rotation;
			_ghostPreview.QueueFree();
			_ghostPreview = null;
			// Ghost will be recreated in next frame's UpdateGhostPosition
		}
	}

	private void PlaceStructure()
	{
		if (_ghostPreview == null) return;

		var pos = _ghostPreview.GlobalPosition;
		var rotY = _ghostPreview.Rotation.Y;

		GameManager.Instance.PlaceBuildStructure(_selectedStructure, pos.X, pos.Y, pos.Z, rotY);
	}

	private void UpdateBuildModeUI()
	{
		if (_buildModeLabel != null)
		{
			if (_buildMode)
			{
				_buildModeLabel.Text = $"BUILD MODE: {_selectedStructure}\n[LMB] Place  [R] Rotate  [[ ]] Change  [B] Exit";
				_buildModeLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
			}
			else
			{
				_buildModeLabel.Text = "[B] Build Mode";
				_buildModeLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
			}
		}
	}
}
