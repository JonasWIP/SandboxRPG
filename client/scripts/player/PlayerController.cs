using Godot;
using SpacetimeDB.Types;
using System;

namespace SandboxRPG;

/// <summary>
/// Controls the local player character.
/// Sends position updates to SpacetimeDB server.
/// Uses client-side prediction with server reconciliation.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    // === Movement Settings ===
    [Export] public float MoveSpeed = 7.0f;
    [Export] public float SprintSpeed = 12.0f;
    [Export] public float JumpVelocity = 6.0f;
    [Export] public float MouseSensitivity = 0.003f;
    [Export] public float Gravity = 20.0f;

    // === Network Settings ===
    [Export] public float PositionSendRate = 0.05f; // 20 updates/sec

    // === Node References ===
    private Camera3D _camera = null!;
    private Node3D _cameraMount = null!;
    private MeshInstance3D _mesh = null!;
    private float _sendTimer;
    private Vector3 _lastSentPosition;
    private float _lastSentRotation;
    private float _cameraRotationX;

    public override void _Ready()
    {
        // Create camera rig
        _cameraMount = new Node3D { Name = "CameraMount" };
        AddChild(_cameraMount);
        _cameraMount.Position = new Vector3(0, 1.6f, 0);

        _camera = new Camera3D { Name = "PlayerCamera" };
        _cameraMount.AddChild(_camera);
        _camera.Position = new Vector3(0, 0, 0);
        _camera.Current = true;

        // Create player mesh (capsule placeholder)
        _mesh = new MeshInstance3D { Name = "PlayerMesh" };
        AddChild(_mesh);
        var capsule = new CapsuleMesh
        {
            Radius = 0.35f,
            Height = 1.8f,
        };
        _mesh.Mesh = capsule;
        _mesh.Position = new Vector3(0, 0.9f, 0);

        // Create stylized material
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 0.7f, 0.9f),
            Roughness = 0.8f,
        };
        _mesh.MaterialOverride = material;

        // Create collision shape
        var collision = new CollisionShape3D { Name = "CollisionShape" };
        AddChild(collision);
        var shape = new CapsuleShape3D
        {
            Radius = 0.35f,
            Height = 1.8f,
        };
        collision.Shape = shape;
        collision.Position = new Vector3(0, 0.9f, 0);

        // Capture mouse
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Mouse look
        if (@event is InputEventMouseMotion mouseMotion)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);
            _cameraRotationX -= mouseMotion.Relative.Y * MouseSensitivity;
            _cameraRotationX = Mathf.Clamp(_cameraRotationX, -Mathf.Pi / 2.2f, Mathf.Pi / 2.2f);
            _cameraMount.Rotation = new Vector3(_cameraRotationX, 0, 0);
        }

        // Toggle mouse capture
        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Gravity
        if (!IsOnFloor())
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);
        }

        // Jump
        if (Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
        }

        // Movement direction
        Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        bool sprinting = Input.IsKeyPressed(Key.Shift);
        float speed = sprinting ? SprintSpeed : MoveSpeed;

        if (direction != Vector3.Zero)
        {
            Velocity = new Vector3(direction.X * speed, Velocity.Y, direction.Z * speed);
        }
        else
        {
            Velocity = new Vector3(
                Mathf.MoveToward(Velocity.X, 0, speed * dt * 10),
                Velocity.Y,
                Mathf.MoveToward(Velocity.Z, 0, speed * dt * 10)
            );
        }

        MoveAndSlide();

        // Send position to server at fixed rate
        _sendTimer += dt;
        if (_sendTimer >= PositionSendRate)
        {
            _sendTimer = 0;
            SendPositionToServer();
        }
    }

    private void SendPositionToServer()
    {
        var pos = GlobalPosition;
        var rotY = Rotation.Y;

        // Only send if position or rotation changed
        if (pos.DistanceTo(_lastSentPosition) > 0.01f || Mathf.Abs(rotY - _lastSentRotation) > 0.01f)
        {
            GameManager.Instance?.SendMovePlayer(pos.X, pos.Y, pos.Z, rotY);
            _lastSentPosition = pos;
            _lastSentRotation = rotY;
        }
    }
}
