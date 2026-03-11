using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Visual representation of another player.
/// Interpolates between server positions for smooth movement.
/// </summary>
public partial class RemotePlayer : Node3D
{
    [Export] public float InterpolationSpeed = 10.0f;

    public string PlayerName { get; set; } = "Unknown";
    public string IdentityHex { get; set; } = "";

    private Vector3 _targetPosition;
    private float _targetRotY;
    private Label3D _nameLabel = null!;
    private MeshInstance3D _mesh = null!;

    public override void _Ready()
    {
        // Create player mesh
        _mesh = new MeshInstance3D { Name = "RemoteMesh" };
        AddChild(_mesh);
        var capsule = new CapsuleMesh
        {
            Radius = 0.35f,
            Height = 1.8f,
        };
        _mesh.Mesh = capsule;
        _mesh.Position = new Vector3(0, 0.9f, 0);

        // Different color for remote players
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.5f, 0.3f),
            Roughness = 0.8f,
        };
        _mesh.MaterialOverride = material;

        // Name label above head
        _nameLabel = new Label3D
        {
            Name = "NameLabel",
            Text = PlayerName,
            FontSize = 48,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0, 2.2f, 0),
        };
        AddChild(_nameLabel);

        _targetPosition = GlobalPosition;
    }

    public override void _Process(double delta)
    {
        // Smooth interpolation to target position
        GlobalPosition = GlobalPosition.Lerp(_targetPosition, (float)delta * InterpolationSpeed);

        // Smooth rotation
        float currentY = Rotation.Y;
        float diff = Mathf.Wrap(_targetRotY - currentY, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0, currentY + diff * (float)delta * InterpolationSpeed, 0);
    }

    public void UpdateFromServer(float x, float y, float z, float rotY, string name)
    {
        _targetPosition = new Vector3(x, y, z);
        _targetRotY = rotY;

        if (name != PlayerName)
        {
            PlayerName = name;
            if (_nameLabel != null)
                _nameLabel.Text = name;
        }
    }
}
