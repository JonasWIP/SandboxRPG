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

        (_healthBarViewport, _healthBar, _healthSprite) = HealthBarFactory.Create(
            width: 80, height: 8,
            fillColor: new Color(0.2f, 0.8f, 0.2f),
            pixelSize: 0.01f,
            yOffset: barY,
            maxValue: MaxHealth,
            currentValue: Health,
            initiallyVisible: Health < MaxHealth,
            bgAlpha: 0.7f);
        AddChild(_healthBarViewport);
        AddChild(_healthSprite);
    }
}
