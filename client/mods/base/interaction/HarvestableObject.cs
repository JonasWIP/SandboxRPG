using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

public partial class HarvestableObject : StaticBody3D, IInteractable
{
    public ulong WorldObjectId { get; set; }
    public string ObjectType { get; set; } = "";
    public uint Health { get; set; }
    public uint MaxHealth { get; set; }

    private Sprite3D? _healthSprite;
    private ProgressBar? _healthBar;
    private SubViewport? _healthBarViewport;

    public string HintText => $"[LMB] Harvest {ObjectType}";
    public string InteractAction => "primary_attack";

    public bool CanInteract(Player? player)
    {
        return !BuildSystem.IsBuildable(Hotbar.Instance?.ActiveItemType);
    }

    public void Interact(Player? player)
    {
        var toolType = Hotbar.Instance?.ActiveItemType ?? string.Empty;
        GameManager.Instance.HarvestWorldObject(WorldObjectId, toolType);
    }

    public override void _Ready()
    {
        if (MaxHealth == 0) return;
        CreateHealthBar();
    }

    private void CreateHealthBar()
    {
        _healthBarViewport = new SubViewport
        {
            Size = new Vector2I(80, 8),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _healthBar = new ProgressBar
        {
            MinValue = 0, MaxValue = MaxHealth, Value = Health,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(80, 8),
        };
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.2f, 0.2f, 0.2f, 0.7f) };
        var fillStyle = new StyleBoxFlat { BgColor = new Color(0.2f, 0.8f, 0.2f) };
        _healthBar.AddThemeStyleboxOverride("background", bgStyle);
        _healthBar.AddThemeStyleboxOverride("fill", fillStyle);
        _healthBarViewport.AddChild(_healthBar);
        AddChild(_healthBarViewport);

        // Position above the object — estimate height from object type
        float barY = ObjectType switch
        {
            "tree_pine" or "tree_palm" => 5.0f,
            "tree_dead" => 4.0f,
            "rock_large" => 2.0f,
            "rock_small" => 1.2f,
            "bush" => 1.5f,
            _ => 2.0f,
        };

        _healthSprite = new Sprite3D
        {
            Name = "HealthBarSprite",
            Texture = _healthBarViewport.GetTexture(),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.01f,
            Position = new Vector3(0, barY, 0),
            Visible = Health < MaxHealth, // only show when damaged
        };
        AddChild(_healthSprite);
    }
}
