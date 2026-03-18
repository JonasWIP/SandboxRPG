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

    // IInteractable — for dialogue/trade NPCs
    public string HintText
    {
        get
        {
            var cfg = GameManager.Instance.GetNpcConfig(NpcType);
            if (cfg is null) return "";
            var visual = NpcVisualRegistry.Get(NpcType);
            string name = visual?.DisplayName ?? NpcType;
            if (cfg.IsTrader) return $"[E] Trade with {name}";
            if (cfg.HasDialogue) return $"[E] Talk to {name}";
            return "";
        }
    }

    public bool CanInteract(Player? player)
    {
        if (!NpcIsAlive) return false;
        var cfg = GameManager.Instance.GetNpcConfig(NpcType);
        return cfg is not null && (cfg.IsTrader || cfg.HasDialogue);
    }

    public void Interact(Player? player)
    {
        var cfg = GameManager.Instance.GetNpcConfig(NpcType);
        if (cfg is null) return;
        if (cfg.IsTrader)
            UIManager.Instance.Push(new NpcTradePanel(NpcId, NpcType));
        else if (cfg.HasDialogue)
            UIManager.Instance.Push(new NpcDialoguePanel(NpcType));
    }

    // IAttackable — for attackable NPCs
    public string AttackHintText
    {
        get
        {
            var visual = NpcVisualRegistry.Get(NpcType);
            string name = visual?.DisplayName ?? NpcType;
            return $"[LMB] Attack {name}";
        }
    }

    public bool CanAttack(Player? player)
    {
        if (!NpcIsAlive) return false;
        var cfg = GameManager.Instance.GetNpcConfig(NpcType);
        return cfg is not null && cfg.IsAttackable;
    }

    public void Attack(Player? player)
    {
        GameManager.Instance.AttackNpc(NpcId);
    }

    public override void _Ready()
    {
        var visual = NpcVisualRegistry.Get(NpcType);
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
        _healthBarViewport = new SubViewport
        {
            Size = new Vector2I(100, 12),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _healthBar = new ProgressBar
        {
            MinValue = 0, MaxValue = NpcMaxHealth, Value = NpcHealth,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(100, 12),
            Position = Vector2.Zero,
        };
        // Style the bar
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.2f, 0.2f, 0.2f, 0.8f) };
        var fillStyle = new StyleBoxFlat { BgColor = NpcVisualRegistry.Get(NpcType)?.HealthBarColor ?? Colors.Red };
        _healthBar.AddThemeStyleboxOverride("background", bgStyle);
        _healthBar.AddThemeStyleboxOverride("fill", fillStyle);
        _healthBarViewport.AddChild(_healthBar);
        AddChild(_healthBarViewport);

        var healthSprite = new Sprite3D
        {
            Name = "HealthBarSprite",
            Texture = _healthBarViewport.GetTexture(),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.01f,
            Position = new Vector3(0, 2.0f * scale, 0),
            Visible = false, // hidden until damaged
        };
        AddChild(healthSprite);
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
                var healthSprite = GetNodeOrNull<Sprite3D>("HealthBarSprite");
                if (healthSprite != null) healthSprite.Visible = false;
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
        _targetPosition = new Vector3(npc.PosX, npc.PosY, npc.PosZ);
        _targetRotY = npc.RotY;
        NpcHealth = npc.Health;
        NpcMaxHealth = npc.MaxHealth;

        // Handle respawn: NPC came back alive
        if (npc.IsAlive && !NpcIsAlive && _deathHandled)
        {
            _deathHandled = false;
            Visible = true;
            _nameLabel.Visible = true;
            _material.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
            var tint = NpcVisualRegistry.Get(NpcType)?.TintColor ?? Colors.Gray;
            _material.AlbedoColor = tint;
        }

        NpcIsAlive = npc.IsAlive;

        // Update health bar
        if (_healthBar != null)
        {
            _healthBar.MaxValue = npc.MaxHealth;
            _healthBar.Value = npc.Health;
        }
        // Show health bar only when damaged
        var healthSprite = GetNodeOrNull<Sprite3D>("HealthBarSprite");
        if (healthSprite != null)
            healthSprite.Visible = npc.IsAlive && npc.Health < npc.MaxHealth;
    }
}
