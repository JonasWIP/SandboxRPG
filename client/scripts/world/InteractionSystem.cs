using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Handles player interactions with world objects (pickup, use, etc.)
/// Uses raycasting from the camera to detect interactable objects.
/// </summary>
public partial class InteractionSystem : Node
{
    [Export] public float InteractionRange = 5.0f;

    private Camera3D? _camera;
    private Label? _interactionHint;

    public override void _Ready()
    {
        // Create interaction hint UI
        _interactionHint = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.6f,
            AnchorBottom = 0.6f,
            OffsetLeft = -200,
            OffsetRight = 200,
            Visible = false,
        };
        _interactionHint.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _interactionHint.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_interactionHint);
    }

    public override void _Process(double delta)
    {
        // Find the camera
        _camera ??= GetViewport()?.GetCamera3D();
        if (_camera == null) return;

        // Raycast from camera center
        var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        var screenCenter = GetViewport().GetVisibleRect().Size / 2;
        var from = _camera.ProjectRayOrigin(screenCenter);
        var to = from + _camera.ProjectRayNormal(screenCenter) * InteractionRange;

        // For interaction detection, we check nearby world items
        CheckNearbyWorldItems();
    }

    private void CheckNearbyWorldItems()
    {
        var localPlayer = GameManager.Instance.GetLocalPlayer();
        if (localPlayer == null) return;

        var playerPos = new Vector3(localPlayer.PosX, localPlayer.PosY, localPlayer.PosZ);
        WorldItem? closestItem = null;
        float closestDist = InteractionRange;

        foreach (var item in GameManager.Instance.GetAllWorldItems())
        {
            var itemPos = new Vector3(item.PosX, item.PosY, item.PosZ);
            float dist = playerPos.DistanceTo(itemPos);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestItem = item;
            }
        }

        if (closestItem != null && _interactionHint != null)
        {
            _interactionHint.Text = $"[E] Pick up {closestItem.ItemType} x{closestItem.Quantity}";
            _interactionHint.Visible = true;

            if (Input.IsActionJustPressed("interact"))
            {
                GameManager.Instance.PickupWorldItem(closestItem.Id);
            }
        }
        else if (_interactionHint != null)
        {
            _interactionHint.Visible = false;
        }
    }
}
