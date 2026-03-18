using Godot;

namespace SandboxRPG;

public partial class InteractionSystem : Node
{
    [Export] public float InteractionRange = 5.0f;

    private Camera3D? _camera;
    private Label? _interactionHint;

    public override void _Ready()
    {
        _interactionHint = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.6f, AnchorBottom = 0.6f,
            OffsetLeft = -200, OffsetRight = 200,
            Visible = false,
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
        var to = from + _camera.ProjectRayNormal(screenCenter) * InteractionRange;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 3;
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0 || !result.ContainsKey("collider"))
        { HideHint(); return; }

        var collider = result["collider"].As<Node>();
        if (collider == null) { HideHint(); return; }

        var interactable = FindInteractable(collider);
        if (interactable == null) { HideHint(); return; }

        var player = GameManager.Instance.GetLocalPlayer();

        if (!interactable.CanInteract(player))
        { ShowHint("[Private]"); return; }

        ShowHint(interactable.HintText);

        if (Input.IsActionJustPressed(interactable.InteractAction))
            interactable.Interact(player);
    }

    private static IInteractable? FindInteractable(Node node)
    {
        Node? current = node;
        for (int i = 0; i < 4 && current != null; i++)
        {
            if (current is IInteractable interactable)
                return interactable;
            current = current.GetParent();
        }
        return null;
    }

    private void ShowHint(string text)
    {
        if (_interactionHint == null) return;
        _interactionHint.Text = text;
        _interactionHint.Visible = true;
    }

    private void HideHint()
    {
        if (_interactionHint != null) _interactionHint.Visible = false;
    }
}
