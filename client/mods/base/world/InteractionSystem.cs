using Godot;

namespace SandboxRPG;

/// <summary>
/// Handles player interactions via a single centre-screen raycast.
/// Layer 1 = terrain / world objects / structures.
/// Layer 2 = world items (pickup).
/// Priority: world item > world object (harvest).
/// </summary>
public partial class InteractionSystem : Node
{
    [Export] public float InteractionRange = 5.0f;

    private Camera3D? _camera;
    private Label?    _interactionHint;

    public override void _Ready()
    {
        _interactionHint = new Label
        {
            Text                = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft          = 0.5f,
            AnchorRight         = 0.5f,
            AnchorTop           = 0.6f,
            AnchorBottom        = 0.6f,
            OffsetLeft          = -200,
            OffsetRight         = 200,
            Visible             = false,
        };
        _interactionHint.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _interactionHint.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_interactionHint);
    }

    public override void _Process(double delta)
    {
        _camera ??= GetViewport()?.GetCamera3D();
        if (_camera == null) return;

        var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        var screenCenter = GetViewport().GetVisibleRect().Size / 2;
        var from = _camera.ProjectRayOrigin(screenCenter);
        var to   = from + _camera.ProjectRayNormal(screenCenter) * InteractionRange;

        // Single raycast — hits items (layer 2) and world objects (layer 1)
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 3; // layers 1 + 2
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0 || !result.ContainsKey("collider"))
        {
            HideHint();
            return;
        }

        var collider = result["collider"].As<Node>();
        if (collider == null) { HideHint(); return; }

        // World item — pick up with E
        if (collider.HasMeta("world_item_id"))
        {
            var itemId   = (ulong)collider.GetMeta("world_item_id").AsInt64();
            var itemType = collider.GetMeta("item_type", "item").AsString();

            // Find live quantity from server data
            uint qty = 1;
            foreach (var wi in GameManager.Instance.GetAllWorldItems())
                if (wi.Id == itemId) { qty = wi.Quantity; break; }

            ShowHint($"[E] Pick up {itemType} x{qty}");

            if (Input.IsActionJustPressed("interact"))
                GameManager.Instance.PickupWorldItem(itemId);

            return;
        }

        // World object — harvest with LMB
        if (collider.IsInGroup("world_object"))
        {
            var objectType = collider.GetMeta("object_type", "object").AsString();
            ShowHint($"[LMB] Harvest {objectType}");

            if (Input.IsActionJustPressed("primary_attack") && !BuildSystem.IsBuildable(Hotbar.Instance?.ActiveItemType))
            {
                var id       = (ulong)collider.GetMeta("world_object_id", 0L).AsInt64();
                var toolType = Hotbar.Instance?.ActiveItemType ?? string.Empty;
                GameManager.Instance.HarvestWorldObject(id, toolType);
            }

            return;
        }

        HideHint();
    }

    private void ShowHint(string text)
    {
        if (_interactionHint == null) return;
        _interactionHint.Text    = text;
        _interactionHint.Visible = true;
    }

    private void HideHint()
    {
        if (_interactionHint != null) _interactionHint.Visible = false;
    }
}
