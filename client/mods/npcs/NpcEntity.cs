// client/mods/npcs/NpcEntity.cs
using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

public partial class NpcEntity : StaticBody3D, IInteractable, IAttackable
{
    [Export] public float InterpolationSpeed = 10.0f;

    public ulong NpcId { get; set; }
    public string NpcType { get; set; } = "";
    public bool NpcIsAlive { get; set; } = true;
    public int NpcHealth { get; set; }
    public int NpcMaxHealth { get; set; }

    private Vector3 _targetPosition;
    private float _targetRotY;
    private Label3D _nameLabel = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private ProgressBar? _healthBar;
    private SubViewport? _healthBarViewport;
    private Sprite3D? _healthSprite;
    private NpcConfig? _cachedConfig;
    private NpcVisualDef? _cachedVisual;

    // IInteractable — for dialogue/trade NPCs
    public string HintText
    {
        get
        {
            if (_cachedConfig is null) return "";
            string name = _cachedVisual?.DisplayName ?? NpcType;
            if (_cachedConfig.IsTrader) return $"[E] Trade with {name}";
            if (_cachedConfig.HasDialogue) return $"[E] Talk to {name}";
            return "";
        }
    }

    public bool CanInteract(Player? player)
    {
        if (!NpcIsAlive) return false;
        return _cachedConfig is not null && (_cachedConfig.IsTrader || _cachedConfig.HasDialogue);
    }

    public void Interact(Player? player)
    {
        if (_cachedConfig is null) return;
        if (_cachedConfig.IsTrader)
            UIManager.Instance.Push(new NpcTradePanel(NpcId, NpcType));
        else if (_cachedConfig.HasDialogue)
            UIManager.Instance.Push(new NpcDialoguePanel(NpcType));
    }

    // IAttackable — for attackable NPCs
    public string AttackHintText
    {
        get
        {
            string name = _cachedVisual?.DisplayName ?? NpcType;
            return $"[LMB] Attack {name}";
        }
    }

    public bool CanAttack(Player? player)
    {
        if (!NpcIsAlive) return false;
        return _cachedConfig is not null && _cachedConfig.IsAttackable;
    }

    public void Attack(Player? player)
    {
        GameManager.Instance.AttackNpc(NpcId);
    }

    public override void _Ready()
    {
        _cachedConfig = GameManager.Instance.GetNpcConfig(NpcType);
        _cachedVisual = NpcVisualRegistry.Get(NpcType);

        var visual = _cachedVisual;
        float scale = visual?.Scale ?? 1.0f;
        var tint = visual?.TintColor ?? Colors.Gray;

        // Mesh (capsule, like players)
        _mesh = new MeshInstance3D { Name = "NpcMesh" };
        _mesh.Mesh = new CapsuleMesh { Radius = 0.35f * scale, Height = 1.8f * scale };
        _mesh.Position = new Vector3(0, 0.9f * scale, 0);
        _material = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.8f };
        _mesh.MaterialOverride = _material;
        AddChild(_mesh);

        // Collision
        var collision = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f * scale, Height = 1.8f * scale },
            Position = new Vector3(0, 0.9f * scale, 0),
        };
        AddChild(collision);

        // Name label
        string displayName = visual?.DisplayName ?? NpcType;
        _nameLabel = new Label3D
        {
            Name = "NameLabel",
            Text = displayName,
            FontSize = 48,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0, 2.2f * scale, 0),
        };
        AddChild(_nameLabel);

        // Health bar (3D billboard sprite using SubViewport)
        CreateHealthBar(scale);

        _targetPosition = GlobalPosition;
        AddToGroup("npc");
    }

    private void CreateHealthBar(float scale)
    {
        var fillColor = _cachedVisual?.HealthBarColor ?? Colors.Red;
        (_healthBarViewport, _healthBar, _healthSprite) = HealthBarFactory.Create(
            width: 100, height: 12,
            fillColor: fillColor,
            pixelSize: 0.01f,
            yOffset: 2.0f * scale,
            maxValue: NpcMaxHealth,
            currentValue: NpcHealth,
            initiallyVisible: false);
        AddChild(_healthBarViewport);
        AddChild(_healthSprite);
    }

    private bool _deathHandled;

    public override void _Process(double delta)
    {
        if (!NpcIsAlive)
        {
            // Hide name label and health bar immediately on death
            if (!_deathHandled)
            {
                _deathHandled = true;
                _nameLabel.Visible = false;
                if (_healthSprite != null) _healthSprite.Visible = false;
                _material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            }

            // Fade out mesh, then free
            if (_material.AlbedoColor.A > 0.01f)
            {
                var c = _material.AlbedoColor;
                c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 2f);
                _material.AlbedoColor = c;
            }
            else
            {
                Visible = false; // fully hidden after fade
            }
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(_targetPosition, (float)delta * InterpolationSpeed);
        float currentY = Rotation.Y;
        float diff = Mathf.Wrap(_targetRotY - currentY, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0, currentY + diff * (float)delta * InterpolationSpeed, 0);
    }

    public void UpdateFromServer(Npc npc)
    {
        // Server sends Y=0 for ground-level NPCs; use terrain height instead
        float y = npc.PosY > 0.01f ? npc.PosY : Terrain.HeightAt(npc.PosX, npc.PosZ);
        _targetPosition = new Vector3(npc.PosX, y, npc.PosZ);
        _targetRotY = npc.RotY;
        NpcHealth = npc.Health;
        NpcMaxHealth = npc.MaxHealth;

        // Handle respawn: NPC came back alive — teleport immediately, don't lerp
        if (npc.IsAlive && !NpcIsAlive && _deathHandled)
        {
            _deathHandled = false;
            Visible = true;
            _nameLabel.Visible = true;
            _material.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
            _material.AlbedoColor = _cachedVisual?.TintColor ?? Colors.Gray;
            GlobalPosition = _targetPosition;
        }

        NpcIsAlive = npc.IsAlive;

        // Update health bar
        if (_healthBar != null)
        {
            _healthBar.MaxValue = npc.MaxHealth;
            _healthBar.Value = npc.Health;
        }
        // Show health bar only when damaged
        if (_healthSprite != null)
            _healthSprite.Visible = npc.IsAlive && npc.Health < npc.MaxHealth;
    }
}
