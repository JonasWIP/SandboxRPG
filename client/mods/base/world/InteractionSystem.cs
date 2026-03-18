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

        // Don't process interactions while UI is open
        if (UIManager.Instance.IsAnyOpen)
        { HideHint(); return; }

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
        var attackable = FindAttackable(collider);

        if (interactable == null && attackable == null) { HideHint(); return; }

        var player = GameManager.Instance.GetLocalPlayer();

        // Interaction takes priority for hint display
        if (interactable != null && interactable.CanInteract(player))
        {
            string hint = interactable.HintText;
            if (attackable != null && attackable.CanAttack(player))
                hint += $"\n{attackable.AttackHintText}";
            ShowHint(hint);

            if (Input.IsActionJustPressed(interactable.InteractAction))
                interactable.Interact(player);
            if (attackable != null && Input.IsActionJustPressed("primary_attack") && attackable.CanAttack(player))
                attackable.Attack(player);
        }
        else if (attackable != null && attackable.CanAttack(player))
        {
            ShowHint(attackable.AttackHintText);
            if (Input.IsActionJustPressed("primary_attack"))
                attackable.Attack(player);
        }
        else if (interactable != null && attackable == null)
        {
            // Only show "[Private]" for pure interactables (e.g. locked chests),
            // not for attackable-only entities like wolves
            ShowHint("[Private]");
        }
        else
        {
            HideHint();
        }
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

    private static IAttackable? FindAttackable(Node node)
    {
        Node? current = node;
        for (int i = 0; i < 4 && current != null; i++)
        {
            if (current is IAttackable attackable)
                return attackable;
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
