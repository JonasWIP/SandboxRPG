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
    public string ColorHex { get; set; } = "#E6804D";

    private Vector3 _targetPosition;
    private float _targetRotY;
    private Label3D _nameLabel = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;

    public override void _Ready()
    {
        // Mesh
        _mesh = new MeshInstance3D { Name = "RemoteMesh" };
        AddChild(_mesh);
        _mesh.Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f };
        _mesh.Position = new Vector3(0, 0.9f, 0);

        _material = new StandardMaterial3D { Roughness = 0.8f };
        _mesh.MaterialOverride = _material;

        // Apply initial color
        SetColor(ColorHex);

        // Name label
        _nameLabel = new Label3D
        {
            Name      = "NameLabel",
            Text      = PlayerName,
            FontSize  = 48,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position  = new Vector3(0, 2.2f, 0),
        };
        AddChild(_nameLabel);

        _targetPosition = GlobalPosition;
    }

    public override void _Process(double delta)
    {
        GlobalPosition = GlobalPosition.Lerp(_targetPosition, (float)delta * InterpolationSpeed);

        float currentY = Rotation.Y;
        float diff = Mathf.Wrap(_targetRotY - currentY, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0, currentY + diff * (float)delta * InterpolationSpeed, 0);
    }

    public void UpdateFromServer(float x, float y, float z, float rotY, string name, string colorHex)
    {
        _targetPosition = new Vector3(x, y, z);
        _targetRotY = rotY;

        if (name != PlayerName)
        {
            PlayerName = name;
            if (_nameLabel != null)
                _nameLabel.Text = name;
        }

        if (colorHex != ColorHex)
            SetColor(colorHex);
    }

    public void SetColor(string colorHex)
    {
        ColorHex = colorHex;
        if (_material is null) return;
        if (Color.HtmlIsValid(colorHex))
            _material.AlbedoColor = new Color(colorHex);
    }
}
