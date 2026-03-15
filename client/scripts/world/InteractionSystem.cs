using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Handles player interactions:
/// - Proximity detection for nearby WorldItems (pickup with E)
/// - Raycast detection for WorldObjects like trees and rocks (harvest with LMB)
/// </summary>
public partial class InteractionSystem : Node
{
    [Export] public float InteractionRange = 5.0f;

    private Camera3D? _camera;
    private Label? _interactionHint;

    // ── Structure handler registry (casino mod registers here) ───────────────
    private static readonly Dictionary<string, Action<ulong>> _structureHandlers = new();

    public static void RegisterStructureHandler(string structureType, Action<ulong> handler)
        => _structureHandlers[structureType] = handler;

    public override void _Ready()
    {
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
        _camera ??= GetViewport()?.GetCamera3D();
        if (_camera == null) return;

        var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        var screenCenter = GetViewport().GetVisibleRect().Size / 2;
        var from = _camera.ProjectRayOrigin(screenCenter);
        var to = from + _camera.ProjectRayNormal(screenCenter) * InteractionRange;

        // World items (proximity) take priority
        if (CheckNearbyWorldItems()) return;

        // Structure interaction (casino machines via Area3D raycast)
        if (CheckStructureRaycast(spaceState, from, to)) return;

        // Fall back to raycast for world objects (trees, rocks, bushes)
        CheckWorldObjectRaycast(spaceState, from, to);
    }

    // Returns true if a nearby world item was found and hint shown
    private bool CheckNearbyWorldItems()
    {
        var localPlayer = GameManager.Instance.GetLocalPlayer();
        if (localPlayer == null) return false;

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

        if (closestItem == null)
        {
            if (_interactionHint != null) _interactionHint.Visible = false;
            return false;
        }

        if (_interactionHint != null)
        {
            _interactionHint.Text = $"[E] Pick up {closestItem.ItemType} x{closestItem.Quantity}";
            _interactionHint.Visible = true;
        }

        if (Input.IsActionJustPressed("interact"))
            GameManager.Instance.PickupWorldItem(closestItem.Id);

        return true;
    }

    private void CheckWorldObjectRaycast(PhysicsDirectSpaceState3D spaceState, Vector3 from, Vector3 to)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0 || !result.ContainsKey("collider"))
        {
            if (_interactionHint != null) _interactionHint.Visible = false;
            return;
        }

        var collider = result["collider"].As<Node>();
        if (collider == null || !collider.IsInGroup("world_object"))
        {
            if (_interactionHint != null) _interactionHint.Visible = false;
            return;
        }

        var objectType = collider.GetMeta("object_type", "object").AsString();
        if (_interactionHint != null)
        {
            _interactionHint.Text = $"[LMB] Harvest {objectType}";
            _interactionHint.Visible = true;
        }

        if (Input.IsActionJustPressed("primary_attack") && !BuildSystem.IsBuildable(Hotbar.Instance?.ActiveItemType))
        {
            var id       = (ulong)collider.GetMeta("world_object_id", 0L).AsInt64();
            var toolType = Hotbar.Instance?.ActiveItemType ?? string.Empty;
            GameManager.Instance.HarvestWorldObject(id, toolType);
        }
    }

    // Returns true if a casino structure was aimed at and hint shown
    private bool CheckStructureRaycast(PhysicsDirectSpaceState3D spaceState, Vector3 from, Vector3 to)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 2; // layer 2 = casino machine interaction
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0 || !result.ContainsKey("collider"))
        {
            return false;
        }

        // Walk up the node tree to find the node with structure metadata
        var collider = result["collider"].As<Node>();
        Node? current = collider;
        while (current != null)
        {
            if (current.HasMeta("structure_id") && current.HasMeta("structure_type"))
            {
                var structId = (ulong)current.GetMeta("structure_id").AsInt64();
                var structType = current.GetMeta("structure_type").AsString();

                if (_structureHandlers.ContainsKey(structType))
                {
                    if (_interactionHint != null)
                    {
                        _interactionHint.Text = $"[E] Use {structType.Replace("casino_", "").Replace("_", " ")}";
                        _interactionHint.Visible = true;
                    }

                    if (Input.IsActionJustPressed("interact"))
                        _structureHandlers[structType](structId);

                    return true;
                }
                break;
            }
            current = current.GetParent();
        }
        return false;
    }
}
