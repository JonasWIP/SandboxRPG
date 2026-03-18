# Currency Mod, Interactables Mod & Engine Infrastructure — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add engine-level interaction, UI, access control, and container systems, then build a currency mod and an interactables mod (chest, crafting table, furnace, sign) on top.

**Architecture:** Engine infrastructure (server tables + client UI framework) goes in `server/` and `client/scripts/`. Mods extend via `IMod`/`IClientMod` interfaces, registering content and hooks during `Seed()`/`Initialize()`. All server tables nest inside `partial class Module`. Client UI uses `BasePanel`/`UIManager` stack pattern.

**Tech Stack:** Godot 4.6.1 C#, SpacetimeDB 2.0 (C# WASM server), .NET 8

**Spec:** `docs/superpowers/specs/2026-03-18-currency-interactables-engine-design.md`

---

## Chunk 1: Server Engine Infrastructure

### Task 1: AccessControl table, helper, and reducer

**Files:**
- Create: `server/AccessControl.cs`

This file contains the `EntityTables` constants, the `AccessControl` table struct, the `AccessControlHelper` static class, and the `ToggleAccessControl` reducer. All inside `partial class Module`.

- [ ] **Step 1: Create `server/AccessControl.cs`**

```csharp
// server/AccessControl.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    /// <summary>String constants for entity table cross-references.</summary>
    public static class EntityTables
    {
        public const string PlacedStructure = "placed_structure";
        public const string WorldObject = "world_object";
    }

    [Table(Name = "access_control", Public = true)]
    public partial struct AccessControl
    {
        [AutoInc][PrimaryKey] public ulong Id;
        public ulong EntityId;
        public string EntityTable; // EntityTables.PlacedStructure or EntityTables.WorldObject
        public Identity OwnerId;
        public bool IsPublic;
    }

    /// <summary>
    /// Checks whether ctx.Sender can access the given entity.
    /// No row = allow (backward compatible). Public = allow. Owner = allow.
    /// </summary>
    public static class AccessControlHelper
    {
        public static bool CanAccess(ReducerContext ctx, ulong entityId, string entityTable)
        {
            foreach (var ac in ctx.Db.AccessControl.Iter())
            {
                if (ac.EntityId == entityId && ac.EntityTable == entityTable)
                {
                    if (ac.IsPublic) return true;
                    return ac.OwnerId == ctx.Sender;
                }
            }
            return true; // no row = allow
        }

        public static AccessControl? Find(ReducerContext ctx, ulong entityId, string entityTable)
        {
            foreach (var ac in ctx.Db.AccessControl.Iter())
                if (ac.EntityId == entityId && ac.EntityTable == entityTable)
                    return ac;
            return null;
        }
    }

    [Reducer]
    public static void ToggleAccessControl(ReducerContext ctx, ulong entityId, string entityTable)
    {
        var ac = AccessControlHelper.Find(ctx, entityId, entityTable);
        if (ac is null)
            throw new System.Exception("No access control entry found.");

        var row = ac.Value;
        if (row.OwnerId != ctx.Sender)
            throw new System.Exception("Only the owner can toggle access control.");

        row.IsPublic = !row.IsPublic;
        ctx.Db.AccessControl.Id.Update(row);
        Log.Info($"Access control toggled: entity {entityId} in {entityTable} -> IsPublic={row.IsPublic}");
    }
}
```

- [ ] **Step 2: Build server to verify**

Run: `cd server && spacetime build`
Expected: Build succeeds (0 errors).

- [ ] **Step 3: Commit**

```bash
git add server/AccessControl.cs
git commit -m "feat(server): add AccessControl table, helper, and ToggleAccessControl reducer"
```

---

### Task 2: ContainerSystem table, config, and reducers

**Files:**
- Create: `server/ContainerSystem.cs`

Contains `ContainerSlot` table, `ContainerConfig` static registry, and `ContainerDeposit`/`ContainerWithdraw`/`ContainerTransfer` reducers.

- [ ] **Step 1: Create `server/ContainerSystem.cs`**

```csharp
// server/ContainerSystem.cs
using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "container_slot", Public = true)]
    public partial struct ContainerSlot
    {
        [AutoInc][PrimaryKey] public ulong Id;
        public ulong ContainerId;
        public string ContainerTable; // EntityTables.PlacedStructure or EntityTables.WorldObject
        public int Slot;
        public string ItemType;
        public uint Quantity;
    }

    [Reducer]
    public static void ContainerDeposit(ReducerContext ctx, ulong containerId, string containerTable, ulong inventoryItemId, int toSlot, uint quantity)
    {
        if (!AccessControlHelper.CanAccess(ctx, containerId, containerTable))
            throw new Exception("Access denied.");

        var invItem = ctx.Db.InventoryItem.Id.Find(inventoryItemId);
        if (invItem is null) throw new Exception("Inventory item not found.");
        var src = invItem.Value;
        if (src.OwnerId != ctx.Sender) throw new Exception("Not your item.");
        if (quantity == 0 || quantity > src.Quantity) throw new Exception("Invalid quantity.");

        // Look up structure type to get slot count from ContainerConfig
        string structureType = "";
        if (containerTable == EntityTables.PlacedStructure)
        {
            var ps = ctx.Db.PlacedStructure.Id.Find(containerId);
            if (ps is not null) structureType = ps.Value.StructureType;
        }
        int slotCount = ContainerConfig.GetSlotCount(structureType);
        if (slotCount == 0) throw new Exception("Not a container.");
        if (toSlot < 0 || toSlot >= slotCount) throw new Exception("Invalid slot.");

        // Find existing container slot content
        ContainerSlot? existing = null;
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
            if (cs.ContainerId == containerId && cs.ContainerTable == containerTable && cs.Slot == toSlot)
            { existing = cs; break; }

        if (existing is not null)
        {
            var ex = existing.Value;
            if (!string.IsNullOrEmpty(ex.ItemType) && ex.ItemType != src.ItemType)
                throw new Exception("Slot occupied by different item type.");
            // Merge or fill empty slot
            ex.ItemType = src.ItemType;
            ex.Quantity += quantity;
            ctx.Db.ContainerSlot.Id.Update(ex);
        }
        else
        {
            ctx.Db.ContainerSlot.Insert(new ContainerSlot
            {
                ContainerId = containerId,
                ContainerTable = containerTable,
                Slot = toSlot,
                ItemType = src.ItemType,
                Quantity = quantity,
            });
        }

        // Consume from inventory
        if (quantity >= src.Quantity)
            ctx.Db.InventoryItem.Delete(src);
        else
        {
            src.Quantity -= quantity;
            ctx.Db.InventoryItem.Id.Update(src);
        }
    }

    [Reducer]
    public static void ContainerWithdraw(ReducerContext ctx, ulong containerId, string containerTable, int fromSlot, uint quantity)
    {
        if (!AccessControlHelper.CanAccess(ctx, containerId, containerTable))
            throw new Exception("Access denied.");

        ContainerSlot? found = null;
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
            if (cs.ContainerId == containerId && cs.ContainerTable == containerTable && cs.Slot == fromSlot)
            { found = cs; break; }

        if (found is null) throw new Exception("Container slot is empty.");
        var slot = found.Value;
        if (quantity == 0 || quantity > slot.Quantity) throw new Exception("Invalid quantity.");

        // Add to player inventory — stack with existing if possible
        bool stacked = false;
        foreach (var inv in ctx.Db.InventoryItem.Iter())
        {
            if (inv.OwnerId == ctx.Sender && inv.ItemType == slot.ItemType)
            {
                var updated = inv;
                updated.Quantity += quantity;
                ctx.Db.InventoryItem.Id.Update(updated);
                stacked = true;
                break;
            }
        }
        if (!stacked)
        {
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender,
                ItemType = slot.ItemType,
                Quantity = quantity,
                Slot = FindOpenHotbarSlot(ctx, ctx.Sender),
            });
        }

        // Remove from container
        if (quantity >= slot.Quantity)
            ctx.Db.ContainerSlot.Delete(slot);
        else
        {
            slot.Quantity -= quantity;
            ctx.Db.ContainerSlot.Id.Update(slot);
        }
    }

    [Reducer]
    public static void ContainerTransfer(ReducerContext ctx, ulong containerId, string containerTable, int fromSlot, int toSlot)
    {
        if (!AccessControlHelper.CanAccess(ctx, containerId, containerTable))
            throw new Exception("Access denied.");

        ContainerSlot? srcSlot = null, dstSlot = null;
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
        {
            if (cs.ContainerId != containerId || cs.ContainerTable != containerTable) continue;
            if (cs.Slot == fromSlot) srcSlot = cs;
            if (cs.Slot == toSlot) dstSlot = cs;
        }

        if (srcSlot is null) throw new Exception("Source slot is empty.");
        var src = srcSlot.Value;

        if (dstSlot is null)
        {
            // Move to empty slot
            src.Slot = toSlot;
            ctx.Db.ContainerSlot.Id.Update(src);
        }
        else
        {
            var dst = dstSlot.Value;
            if (dst.ItemType == src.ItemType)
            {
                // Merge
                dst.Quantity += src.Quantity;
                ctx.Db.ContainerSlot.Id.Update(dst);
                ctx.Db.ContainerSlot.Delete(src);
            }
            else
            {
                // Swap
                int tempSlot = src.Slot;
                src.Slot = dst.Slot;
                dst.Slot = tempSlot;
                ctx.Db.ContainerSlot.Id.Update(src);
                ctx.Db.ContainerSlot.Id.Update(dst);
            }
        }
    }
}

/// <summary>
/// In-memory registry mapping container TYPE STRINGS to their slot counts.
/// Populated by mods during Seed(). Repopulated on every server restart.
/// Keyed by structure type (e.g. "chest" -> 16, "furnace" -> 2), NOT entity ID.
/// </summary>
public static class ContainerConfig
{
    private static readonly Dictionary<string, int> _slotCounts = new();

    public static void Register(string structureType, int slotCount)
        => _slotCounts[structureType] = slotCount;

    public static int GetSlotCount(string structureType)
        => _slotCounts.TryGetValue(structureType, out var n) ? n : 0;

    public static void Clear() => _slotCounts.Clear();
}
```

- [ ] **Step 2: Build server to verify**

Run: `cd server && spacetime build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add server/ContainerSystem.cs
git commit -m "feat(server): add ContainerSlot table, ContainerConfig registry, and container reducers"
```

---

### Task 3: StructureHooks — placement and removal hook registry

**Files:**
- Create: `server/StructureHooks.cs`
- Modify: `mods/base/server/BuildingReducers.cs` — call hooks after place / before remove

- [ ] **Step 1: Create `server/StructureHooks.cs`**

```csharp
// server/StructureHooks.cs
using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    public static class StructureHooks
    {
        private static readonly Dictionary<string, List<Action<ReducerContext, PlacedStructure>>> _onPlace = new();
        private static readonly Dictionary<string, List<Action<ReducerContext, PlacedStructure>>> _onRemove = new();

        public static void RegisterOnPlace(string structureType, Action<ReducerContext, PlacedStructure> hook)
        {
            if (!_onPlace.ContainsKey(structureType))
                _onPlace[structureType] = new();
            _onPlace[structureType].Add(hook);
        }

        public static void RegisterOnRemove(string structureType, Action<ReducerContext, PlacedStructure> hook)
        {
            if (!_onRemove.ContainsKey(structureType))
                _onRemove[structureType] = new();
            _onRemove[structureType].Add(hook);
        }

        public static void RunOnPlace(ReducerContext ctx, PlacedStructure structure)
        {
            if (_onPlace.TryGetValue(structure.StructureType, out var hooks))
                foreach (var hook in hooks) hook(ctx, structure);
        }

        public static void RunOnRemove(ReducerContext ctx, PlacedStructure structure)
        {
            if (_onRemove.TryGetValue(structure.StructureType, out var hooks))
                foreach (var hook in hooks) hook(ctx, structure);
        }

        public static void Clear()
        {
            _onPlace.Clear();
            _onRemove.Clear();
        }
    }
}
```

- [ ] **Step 2: Modify `mods/base/server/BuildingReducers.cs` — add hook calls**

In `PlaceStructure`, after `ctx.Db.PlacedStructure.Insert(...)`, the inserted row doesn't return the auto-incremented ID directly. We need to find the just-inserted structure. Add after line 42 (after Insert, before consuming item):

```csharp
        // Find the just-inserted structure to get its auto-incremented ID
        PlacedStructure? inserted = null;
        foreach (var ps in ctx.Db.PlacedStructure.Iter())
        {
            if (ps.OwnerId == identity && ps.StructureType == structureType
                && ps.PosX == posX && ps.PosY == posY && ps.PosZ == posZ)
            {
                inserted = ps;
                break;
            }
        }
        if (inserted is not null)
            StructureHooks.RunOnPlace(ctx, inserted.Value);
```

In `RemoveStructure`, add before line 76 (`ctx.Db.PlacedStructure.Delete(s)`):

```csharp
        StructureHooks.RunOnRemove(ctx, s);
```

- [ ] **Step 3: Build server to verify**

Run: `cd server && spacetime build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add server/StructureHooks.cs mods/base/server/BuildingReducers.cs
git commit -m "feat(server): add StructureHooks registry, integrate into PlaceStructure and RemoveStructure"
```

---

### Task 4: Add Station field to CraftingRecipe + CraftItem reducer

**Files:**
- Modify: `mods/base/server/Tables.cs:72-83` — add `Station` field to `CraftingRecipe`
- Modify: `mods/base/server/CraftingReducers.cs:13` — add `station` parameter to `CraftItem`
- Modify: `mods/base/server/BaseMod.cs` — add `Station = ""` to seeded recipes
- Modify: `mods/hello-world/server/HelloWorldMod.cs:23-29` — add `Station = ""`

- [ ] **Step 1: Add `Station` field to `CraftingRecipe` in `mods/base/server/Tables.cs`**

Add after line 82 (`public float CraftTimeSeconds;`):

```csharp
        /// <summary>Station required to craft. Empty string = craftable anywhere.</summary>
        public string Station;
```

- [ ] **Step 2: Add `station` parameter to `CraftItem` in `mods/base/server/CraftingReducers.cs`**

Change line 13 from:
```csharp
    public static void CraftItem(ReducerContext ctx, ulong recipeId)
```
to:
```csharp
    public static void CraftItem(ReducerContext ctx, ulong recipeId, string station)
```

Add station check after line 20 (`var ingredients = ParseIngredients(r.Ingredients);`):
```csharp
        // Station check — recipe requires specific station
        if (!string.IsNullOrEmpty(r.Station) && r.Station != station)
            throw new Exception($"This recipe requires a {r.Station}.");
```

- [ ] **Step 3: Add `Station = ""` to seeded recipes in `mods/base/server/Seeding.cs`**

The `SeedRecipes` method is in `mods/base/server/Seeding.cs` (called from `BaseMod.Seed`). Each `CraftingRecipe` insert (lines 30-38) needs `Station = ""` appended. Add `, Station = ""` to every insert call.

- [ ] **Step 4: Add `Station = ""` to hello-world recipe in `mods/hello-world/server/HelloWorldMod.cs:23-29`**

Add to the `CraftingRecipe` insert:
```csharp
                Station          = "",
```

- [ ] **Step 5: Build server to verify**

Run: `cd server && spacetime build`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add mods/base/server/Tables.cs mods/base/server/CraftingReducers.cs mods/base/server/Seeding.cs mods/hello-world/server/HelloWorldMod.cs
git commit -m "feat(server): add Station field to CraftingRecipe, enforce in CraftItem reducer"
```

---

### Task 5: Regenerate client bindings

After server schema changes, regenerate the SpacetimeDB client bindings.

- [ ] **Step 1: Regenerate bindings**

Run:
```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```
Expected: New binding files generated for `AccessControl`, `ContainerSlot`, and updated `CraftingRecipe` (with `Station` field).

- [ ] **Step 2: Fix `GameManager.CraftRecipe` to match new `CraftItem` signature**

The regenerated bindings now expect `CraftItem(ulong, string)` instead of `CraftItem(ulong)`. Fix `GameManager.cs` line 112 immediately to prevent build failure:

Change:
```csharp
    public void CraftRecipe(ulong id)         => Conn?.Reducers.CraftItem(id);
```
to:
```csharp
    public void CraftRecipe(ulong id, string station = "") => Conn?.Reducers.CraftItem(id, station);
```

- [ ] **Step 3: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds (may have warnings about unused bindings — that's OK).

- [ ] **Step 4: Commit**

```bash
git add client/scripts/networking/SpacetimeDB/ client/scripts/networking/GameManager.cs
git commit -m "chore: regenerate client bindings for AccessControl, ContainerSlot, CraftingRecipe.Station"
```

---

### Task 6: Update GameManager — new signals and accessors for new tables

**Files:**
- Modify: `client/scripts/networking/GameManager.cs`

Add signals, data access methods, and reducer call wrappers for the new tables.

- [ ] **Step 1: Add signals after line 57 (`TerrainConfigChangedEventHandler`)**

```csharp
    [Signal] public delegate void AccessControlChangedEventHandler();
    [Signal] public delegate void ContainerSlotChangedEventHandler();
```

- [ ] **Step 2: Add data access methods after line 147 (`GetTerrainConfig`)**

```csharp
    public IEnumerable<AccessControl>  GetAllAccessControls()  { if (Conn != null) foreach (var a in Conn.Db.AccessControl.Iter()) yield return a; }
    public IEnumerable<ContainerSlot>  GetContainerSlots(ulong containerId)
    {
        if (Conn == null) yield break;
        foreach (var cs in Conn.Db.ContainerSlot.Iter())
            if (cs.ContainerId == containerId) yield return cs;
    }
    public AccessControl? GetAccessControl(ulong entityId, string entityTable)
    {
        if (Conn == null) return null;
        foreach (var ac in Conn.Db.AccessControl.Iter())
            if (ac.EntityId == entityId && ac.EntityTable == entityTable) return ac;
        return null;
    }
```

- [ ] **Step 3: Add reducer call wrappers after line 116 (`HarvestWorldObject`)**

```csharp
    public void ToggleAccess(ulong entityId, string entityTable) => Conn?.Reducers.ToggleAccessControl(entityId, entityTable);
    public void ContainerDeposit(ulong containerId, string containerTable, ulong inventoryItemId, int toSlot, uint quantity)
        => Conn?.Reducers.ContainerDeposit(containerId, containerTable, inventoryItemId, toSlot, quantity);
    public void ContainerWithdraw(ulong containerId, string containerTable, int fromSlot, uint quantity)
        => Conn?.Reducers.ContainerWithdraw(containerId, containerTable, fromSlot, quantity);
    public void ContainerTransfer(ulong containerId, string containerTable, int fromSlot, int toSlot)
        => Conn?.Reducers.ContainerTransfer(containerId, containerTable, fromSlot, toSlot);
```

- [ ] **Step 4: Register callbacks for new tables in `RegisterCallbacks`**

```csharp
        conn.Db.AccessControl.OnInsert += (ctx, _) => CallDeferred(nameof(EmitAccessControlChanged));
        conn.Db.AccessControl.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitAccessControlChanged));
        conn.Db.AccessControl.OnDelete += (ctx, _) => CallDeferred(nameof(EmitAccessControlChanged));

        conn.Db.ContainerSlot.OnInsert += (ctx, _) => CallDeferred(nameof(EmitContainerSlotChanged));
        conn.Db.ContainerSlot.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitContainerSlotChanged));
        conn.Db.ContainerSlot.OnDelete += (ctx, _) => CallDeferred(nameof(EmitContainerSlotChanged));
```

- [ ] **Step 5: Add deferred signal emitters**

```csharp
    private void EmitAccessControlChanged() => EmitSignal(SignalName.AccessControlChanged);
    private void EmitContainerSlotChanged() => EmitSignal(SignalName.ContainerSlotChanged);
```

- [ ] **Step 6: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add client/scripts/networking/GameManager.cs
git commit -m "feat(client): add GameManager signals, accessors, and reducer wrappers for AccessControl and ContainerSlot"
```

---

## Chunk 2: Client Engine Infrastructure — Interaction & UI

### Task 7: IInteractable interface

**Files:**
- Create: `client/scripts/interaction/IInteractable.cs`

- [ ] **Step 1: Create the directory and interface**

```csharp
// client/scripts/interaction/IInteractable.cs
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Interface for any node that can be interacted with via the raycast system.
/// Implement on a Node3D or its parent (max 3 levels up from collider).
/// </summary>
public interface IInteractable
{
    /// <summary>Hint text shown below crosshair, e.g. "[E] Open Chest".</summary>
    string HintText { get; }

    /// <summary>Input action name that triggers this interaction. Default: "interact" (E key).</summary>
    string InteractAction => "interact";

    /// <summary>Whether the given player can interact right now (access control, distance, etc.).</summary>
    bool CanInteract(Player? player);

    /// <summary>Execute the interaction (open UI, pick up, harvest, etc.).</summary>
    void Interact(Player? player);
}
```

- [ ] **Step 2: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/interaction/IInteractable.cs
git commit -m "feat(client): add IInteractable interface for generalized interaction system"
```

---

### Task 8: Refactor InteractionSystem to use IInteractable

**Files:**
- Modify: `client/mods/base/world/InteractionSystem.cs`

Replace hardcoded meta-tag checks with `IInteractable` discovery via parent walk.

- [ ] **Step 1: Rewrite `InteractionSystem.cs`**

```csharp
using Godot;

namespace SandboxRPG;

/// <summary>
/// Handles player interactions via a single centre-screen raycast.
/// Walks up from collider (max 3 levels) to find IInteractable.
/// Layer 1 = terrain/objects/structures. Layer 2 = world items (pickup).
/// </summary>
public partial class InteractionSystem : Node
{
    [Export] public float InteractionRange = 5.0f;

    private Camera3D? _camera;
    private Label?    _interactionHint;
    private IInteractable? _currentTarget;

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

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 3; // layers 1 + 2
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0 || !result.ContainsKey("collider"))
        {
            HideHint();
            _currentTarget = null;
            return;
        }

        var collider = result["collider"].As<Node>();
        if (collider == null) { HideHint(); _currentTarget = null; return; }

        var interactable = FindInteractable(collider);
        if (interactable == null) { HideHint(); _currentTarget = null; return; }

        _currentTarget = interactable;
        var player = GameManager.Instance.GetLocalPlayer();

        if (!interactable.CanInteract(player))
        {
            ShowHint("[Private]");
            return;
        }

        ShowHint(interactable.HintText);

        if (Input.IsActionJustPressed(interactable.InteractAction))
            interactable.Interact(player);
    }

    /// <summary>Walk up from collider via GetParent() (max 3 levels) to find IInteractable.</summary>
    private static IInteractable? FindInteractable(Node node)
    {
        Node? current = node;
        for (int i = 0; i < 4 && current != null; i++) // check node itself + 3 parents
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
        _interactionHint.Text    = text;
        _interactionHint.Visible = true;
    }

    private void HideHint()
    {
        if (_interactionHint != null) _interactionHint.Visible = false;
    }
}
```

- [ ] **Step 2: Make existing spawners implement IInteractable**

The existing `WorldItemSpawner` and `WorldObjectSpawner` create `StaticBody3D` nodes. We need lightweight wrapper scripts that implement `IInteractable` on these nodes. Create two helper scripts:

**Create `client/mods/base/interaction/WorldItemPickup.cs`:**

```csharp
using Godot;

namespace SandboxRPG;

/// <summary>IInteractable wrapper for world items (pickup with E).</summary>
public partial class WorldItemPickup : StaticBody3D, IInteractable
{
    public ulong WorldItemId { get; set; }
    public string ItemType { get; set; } = "";
    public uint Quantity { get; set; } = 1;

    public string HintText => $"[E] Pick up {ItemType} x{Quantity}";
    public string InteractAction => "interact";

    public bool CanInteract(SpacetimeDB.Types.Player? player) => true;

    public void Interact(SpacetimeDB.Types.Player? player)
    {
        GameManager.Instance.PickupWorldItem(WorldItemId);
    }

    /// <summary>Refresh quantity from server data.</summary>
    public void UpdateFromServer()
    {
        foreach (var wi in GameManager.Instance.GetAllWorldItems())
        {
            if (wi.Id == WorldItemId)
            {
                Quantity = wi.Quantity;
                return;
            }
        }
    }
}
```

**Create `client/mods/base/interaction/HarvestableObject.cs`:**

```csharp
using Godot;

namespace SandboxRPG;

/// <summary>IInteractable wrapper for harvestable world objects (LMB to harvest).</summary>
public partial class HarvestableObject : StaticBody3D, IInteractable
{
    public ulong WorldObjectId { get; set; }
    public string ObjectType { get; set; } = "";

    public string HintText => $"[LMB] Harvest {ObjectType}";
    public string InteractAction => "primary_attack";

    public bool CanInteract(SpacetimeDB.Types.Player? player)
    {
        // Don't harvest when holding a buildable item
        return !BuildSystem.IsBuildable(Hotbar.Instance?.ActiveItemType);
    }

    public void Interact(SpacetimeDB.Types.Player? player)
    {
        var toolType = Hotbar.Instance?.ActiveItemType ?? string.Empty;
        GameManager.Instance.HarvestWorldObject(WorldObjectId, toolType);
    }
}
```

- [ ] **Step 3: Update `WorldItemSpawner` to use `WorldItemPickup` instead of plain `StaticBody3D`**

In `client/mods/base/spawners/WorldItemSpawner.cs`, change the node creation to use `WorldItemPickup` instead of `StaticBody3D`. The spawner currently creates a `StaticBody3D` and sets meta tags. Replace with `WorldItemPickup` which carries the data as properties.

Key changes:
- Replace `new StaticBody3D()` with `new WorldItemPickup()`
- Set `WorldItemId`, `ItemType`, `Quantity` properties instead of `SetMeta` calls
- Remove `SetMeta("world_item_id", ...)` and `SetMeta("item_type", ...)`

- [ ] **Step 4: Update `WorldObjectSpawner` to use `HarvestableObject` instead of plain `StaticBody3D`**

In `client/mods/base/spawners/WorldObjectSpawner.cs`, change the node creation to use `HarvestableObject` instead of `StaticBody3D`.

Key changes:
- Replace `new StaticBody3D()` with `new HarvestableObject()`
- Set `WorldObjectId`, `ObjectType` properties instead of `SetMeta` calls
- Keep the `"world_object"` group for backward compatibility

- [ ] **Step 5: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add client/mods/base/world/InteractionSystem.cs \
       client/mods/base/interaction/WorldItemPickup.cs \
       client/mods/base/interaction/HarvestableObject.cs \
       client/mods/base/spawners/WorldItemSpawner.cs \
       client/mods/base/spawners/WorldObjectSpawner.cs
git commit -m "refactor(client): generalize InteractionSystem to use IInteractable interface"
```

---

### Task 9: Extract InventoryGrid from InventoryCraftingPanel

**Files:**
- Create: `client/scripts/ui/InventoryGrid.cs`

Extract the inventory grid rendering + context menu logic from `InventoryCraftingPanel` into a reusable component.

- [ ] **Step 1: Create `client/scripts/ui/InventoryGrid.cs`**

```csharp
// client/scripts/ui/InventoryGrid.cs
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Reusable inventory grid component — renders the player's inventory items
/// in a 4xN grid with right-click context menu (hotbar assign, drop).
/// </summary>
public partial class InventoryGrid : VBoxContainer
{
    private GridContainer _grid = null!;
    private Control?      _contextMenu;

    public override void _Ready()
    {
        var header = UIFactory.MakeLabel("Inventory", 14, UIFactory.ColourAccent);
        AddChild(header);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 400);
        AddChild(scroll);

        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_grid);
    }

    public void Refresh()
    {
        foreach (Node child in _grid.GetChildren())
            child.QueueFree();

        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            var colour = ItemColour(item.ItemType);
            var btn = UIFactory.MakeSlotButton(item.ItemType, item.Quantity, colour);
            btn.CustomMinimumSize = UIFactory.SlotSize;

            ulong itemId  = item.Id;
            uint  itemQty = item.Quantity;

            btn.GuiInput += (evt) =>
            {
                if (evt is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                    ShowContextMenu(mb.GlobalPosition, itemId, itemQty);
            };

            _grid.AddChild(btn);
        }
    }

    // Context menu — identical to the original InventoryCraftingPanel logic
    private void ShowContextMenu(Vector2 screenPos, ulong itemId, uint itemQty)
    {
        CloseContextMenu();

        _contextMenu = new Control();
        _contextMenu.SetAnchorsPreset(LayoutPreset.FullRect);
        _contextMenu.MouseFilter = MouseFilterEnum.Stop;
        _contextMenu.GuiInput += (evt) =>
        {
            if (evt is InputEventMouseButton mb && mb.Pressed)
                CloseContextMenu();
        };

        var popup = new PanelContainer { Position = screenPos };
        _contextMenu.AddChild(popup);

        var col = UIFactory.MakeVBox(4);
        popup.AddChild(col);

        var occupiedSlots = new Dictionary<int, string>();
        foreach (var inv in GameManager.Instance.GetMyInventory())
            if (inv.Slot >= 0 && inv.Slot < Hotbar.SlotCount)
                occupiedSlots[inv.Slot] = inv.ItemType;

        int currentSlot = -1;
        foreach (var inv in GameManager.Instance.GetMyInventory())
            if (inv.Id == itemId) { currentSlot = inv.Slot; break; }

        col.AddChild(UIFactory.MakeLabel("Assign to slot:", 11, UIFactory.ColourMuted));

        var slotRow = UIFactory.MakeHBox(4);
        col.AddChild(slotRow);

        for (int s = 0; s < Hotbar.SlotCount; s++)
        {
            int slot = s;
            bool isCurrent  = slot == currentSlot;
            bool isOccupied = occupiedSlots.ContainsKey(slot) && !isCurrent;

            var slotBtn = UIFactory.MakeButton($"{s + 1}", 12, new Vector2(28, 28));
            if (isCurrent)
                slotBtn.Modulate = UIFactory.ColourAccent;
            else if (isOccupied)
                slotBtn.Modulate = new Color(1f, 0.6f, 0.3f);

            slotBtn.Pressed += () =>
            {
                GameManager.Instance.MoveItemSlot(itemId, slot);
                CloseContextMenu();
            };
            slotRow.AddChild(slotBtn);
        }

        col.AddChild(UIFactory.MakeSeparator());

        var drop1Btn = UIFactory.MakeButton("Drop 1", 13, new Vector2(120, 32));
        drop1Btn.Pressed += () =>
        {
            GameManager.Instance.DropInventoryItem(itemId, 1);
            CloseContextMenu();
        };
        col.AddChild(drop1Btn);

        if (itemQty > 1)
        {
            var dropAllBtn = UIFactory.MakeButton($"Drop All ({itemQty})", 13, new Vector2(120, 32));
            dropAllBtn.Pressed += () =>
            {
                GameManager.Instance.DropInventoryItem(itemId, itemQty);
                CloseContextMenu();
            };
            col.AddChild(dropAllBtn);
        }

        AddChild(_contextMenu);
    }

    private void CloseContextMenu()
    {
        _contextMenu?.QueueFree();
        _contextMenu = null;
    }

    private static Color ItemColour(string itemType) => itemType switch
    {
        "wood"           => new Color("#8B5E3C"),
        "stone"          => new Color("#888899"),
        "iron"           => new Color("#AAAACC"),
        "wood_wall"      => new Color("#7B6B3D"),
        "stone_wall"     => new Color("#777788"),
        "wood_floor"     => new Color("#9B7040"),
        "stone_floor"    => new Color("#8888AA"),
        "wood_door"      => new Color("#6B500E"),
        "campfire"       => new Color("#E55454"),
        "workbench"      => new Color("#8B6040"),
        "chest"          => new Color("#B8862B"),
        "wood_pickaxe"   => new Color("#9B7040"),
        "stone_pickaxe"  => new Color("#777788"),
        "iron_pickaxe"   => new Color("#AAAACC"),
        "wood_axe"       => new Color("#8B5E3C"),
        _                => UIFactory.ColourMuted,
    };
}
```

- [ ] **Step 2: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/ui/InventoryGrid.cs
git commit -m "feat(client): extract InventoryGrid as reusable component from InventoryCraftingPanel"
```

---

### Task 10: InteractionPanel base class

**Files:**
- Create: `client/scripts/ui/InteractionPanel.cs`

- [ ] **Step 1: Create `client/scripts/ui/InteractionPanel.cs`**

```csharp
// client/scripts/ui/InteractionPanel.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Base class for split-screen interaction panels.
/// Left: player inventory (InventoryGrid). Right: context-specific content (mod-provided).
/// Subclasses implement BuildContextSide() to provide the right panel.
/// </summary>
public abstract partial class InteractionPanel : BasePanel
{
    protected InventoryGrid _inventoryGrid = null!;

    public override void OnPushed()
    {
        GameManager.Instance.InventoryChanged    += RefreshAll;
        GameManager.Instance.RecipesLoaded       += RefreshAll;
        GameManager.Instance.SubscriptionApplied += RefreshAll;
        RefreshAll();
    }

    public override void OnPopped()
    {
        GameManager.Instance.InventoryChanged    -= RefreshAll;
        GameManager.Instance.RecipesLoaded       -= RefreshAll;
        GameManager.Instance.SubscriptionApplied -= RefreshAll;
    }

    protected override void BuildUI()
    {
        // Dim backdrop
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var outerPanel = UIFactory.MakePanel(new Vector2(860, 560));
        center.AddChild(outerPanel);

        var root = UIFactory.MakeVBox(10);
        outerPanel.AddChild(root);

        // Title row
        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(titleRow);

        titleRow.AddChild(UIFactory.MakeTitle(PanelTitle, 20));

        var closeBtn = UIFactory.MakeButton("\u2715", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        root.AddChild(UIFactory.MakeSeparator());

        // Two-column row
        var columns = UIFactory.MakeHBox(16);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        root.AddChild(columns);

        // LEFT: Inventory
        _inventoryGrid = new InventoryGrid();
        _inventoryGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(_inventoryGrid);

        // RIGHT: Context
        var rightSide = BuildContextSide();
        rightSide.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(rightSide);
    }

    /// <summary>Title shown at top of panel. Override in subclasses.</summary>
    protected virtual string PanelTitle => "Interaction";

    /// <summary>Build the right-side content. Override in subclasses.</summary>
    protected abstract Control BuildContextSide();

    /// <summary>Called when inventory, recipes, or container data changes.</summary>
    protected virtual void RefreshAll()
    {
        _inventoryGrid?.Refresh();
        RefreshContextSide();
    }

    /// <summary>Refresh the right-side content. Override in subclasses.</summary>
    protected virtual void RefreshContextSide() { }
}
```

- [ ] **Step 2: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/ui/InteractionPanel.cs
git commit -m "feat(client): add InteractionPanel split-screen base class"
```

---

### Task 11: Refactor InventoryCraftingPanel to extend InteractionPanel

**Files:**
- Modify: `client/mods/base/ui/InventoryCraftingPanel.cs`

Rewrite to extend `InteractionPanel` instead of `BasePanel`. The crafting recipe list becomes `BuildContextSide()`. Remove duplicated inventory grid code.

- [ ] **Step 1: Rewrite `InventoryCraftingPanel.cs`**

```csharp
using Godot;
using System.Collections.Generic;
using SandboxRPG;

/// <summary>
/// Combined Inventory + Crafting panel — opened with I or C.
/// Left: InventoryGrid (from InteractionPanel). Right: recipe list.
/// </summary>
public partial class InventoryCraftingPanel : InteractionPanel
{
    private VBoxContainer _recipeList = null!;

    protected override string PanelTitle => "Inventory & Crafting";

    /// <summary>Optional station context — set when opened from a crafting table.</summary>
    public string Station { get; set; } = "";

    protected override Control BuildContextSide()
    {
        var rightCol = UIFactory.MakeVBox(8);

        rightCol.AddChild(UIFactory.MakeLabel("Crafting", 14, UIFactory.ColourAccent));

        var craftScroll = new ScrollContainer();
        craftScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        craftScroll.CustomMinimumSize = new Vector2(0, 400);
        rightCol.AddChild(craftScroll);

        _recipeList = UIFactory.MakeVBox(6);
        craftScroll.AddChild(_recipeList);

        return rightCol;
    }

    protected override void RefreshContextSide()
    {
        RefreshRecipes();
    }

    private void RefreshRecipes()
    {
        foreach (Node child in _recipeList.GetChildren())
            child.QueueFree();

        var have = new Dictionary<string, uint>();
        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            have.TryGetValue(item.ItemType, out uint cur);
            have[item.ItemType] = cur + item.Quantity;
        }

        foreach (var recipe in GameManager.Instance.GetAllRecipes())
        {
            // Hide station-only recipes when not at that station
            bool stationLocked = !string.IsNullOrEmpty(recipe.Station) && recipe.Station != Station;

            var row = UIFactory.MakeHBox(8);
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _recipeList.AddChild(row);

            var infoCol = UIFactory.MakeVBox(2);
            infoCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(infoCol);

            infoCol.AddChild(UIFactory.MakeLabel(
                $"{recipe.ResultItemType.Replace('_', ' ')} \u00d7{recipe.ResultQuantity}", 13));

            var ingredientStr = FormatIngredients(recipe.Ingredients);
            var ingLbl = UIFactory.MakeLabel(ingredientStr, 10, UIFactory.ColourMuted);
            infoCol.AddChild(ingLbl);

            if (stationLocked)
            {
                var stationLbl = UIFactory.MakeLabel($"Requires: {recipe.Station.Replace('_', ' ')}", 10, UIFactory.ColourDanger);
                infoCol.AddChild(stationLbl);
            }

            bool canCraft = !stationLocked && CanCraft(recipe.Ingredients, have);

            var craftBtn = UIFactory.MakeButton("Craft", 13, new Vector2(70, 34));
            craftBtn.Disabled = !canCraft;
            if (!canCraft)
                craftBtn.Modulate = new Color(1, 1, 1, 0.4f);

            ulong recipeId = recipe.Id;
            string station = Station;
            craftBtn.Pressed += () => GameManager.Instance.CraftRecipe(recipeId, station);
            row.AddChild(craftBtn);

            _recipeList.AddChild(UIFactory.MakeSeparator());
        }
    }

    private static string FormatIngredients(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var parts = new List<string>();
        foreach (var part in raw.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2)
                parts.Add($"{kv[0].Trim().Replace('_', ' ')} \u00d7{kv[1].Trim()}");
        }
        return string.Join("  ", parts);
    }

    private static bool CanCraft(string ingredients, Dictionary<string, uint> have)
    {
        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length != 2 || !uint.TryParse(kv[1], out uint need)) continue;
            string type = kv[0].Trim();
            have.TryGetValue(type, out uint owned);
            if (owned < need) return false;
        }
        return true;
    }
}
```

- [ ] **Step 2: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add client/mods/base/ui/InventoryCraftingPanel.cs
git commit -m "refactor(client): rewrite InventoryCraftingPanel to extend InteractionPanel"
```

---

### Task 12: ContainerGrid reusable component

**Files:**
- Create: `client/scripts/ui/ContainerGrid.cs`

- [ ] **Step 1: Create `client/scripts/ui/ContainerGrid.cs`**

```csharp
// client/scripts/ui/ContainerGrid.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Reusable container slot grid — renders container slots and handles
/// click-to-withdraw. Used by ContainerPanel, FurnacePanel, etc.
/// </summary>
public partial class ContainerGrid : VBoxContainer
{
    private GridContainer _grid = null!;
    private ulong _containerId;
    private string _containerTable = "";
    private int _slotCount;
    private string _title = "Container";

    public ContainerGrid(ulong containerId, string containerTable, int slotCount, string title = "Container")
    {
        _containerId = containerId;
        _containerTable = containerTable;
        _slotCount = slotCount;
        _title = title;
    }

    public override void _Ready()
    {
        AddChild(UIFactory.MakeLabel(_title, 14, UIFactory.ColourAccent));

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 400);
        AddChild(scroll);

        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_grid);
    }

    public void Refresh()
    {
        foreach (Node child in _grid.GetChildren())
            child.QueueFree();

        // Build slot map from server data
        var slotMap = new (string itemType, uint quantity, ulong slotId)?[_slotCount];
        foreach (var cs in GameManager.Instance.GetContainerSlots(_containerId))
        {
            if (cs.Slot >= 0 && cs.Slot < _slotCount)
                slotMap[cs.Slot] = (cs.ItemType, cs.Quantity, cs.Id);
        }

        for (int i = 0; i < _slotCount; i++)
        {
            int slot = i;
            if (slotMap[i] is var (itemType, qty, slotId))
            {
                var btn = UIFactory.MakeSlotButton(itemType, qty, UIFactory.ColourMuted);
                btn.CustomMinimumSize = UIFactory.SlotSize;
                btn.Pressed += () =>
                {
                    GameManager.Instance.ContainerWithdraw(_containerId, _containerTable, slot, qty);
                };
                _grid.AddChild(btn);
            }
            else
            {
                // Empty slot — clicking deposits from selected inventory item
                var btn = UIFactory.MakeButton("", 10, UIFactory.SlotSize);
                btn.CustomMinimumSize = UIFactory.SlotSize;
                _grid.AddChild(btn);
            }
        }
    }

    public ulong ContainerId => _containerId;
    public string ContainerTable => _containerTable;
}
```

- [ ] **Step 2: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/ui/ContainerGrid.cs
git commit -m "feat(client): add ContainerGrid reusable component for container UIs"
```

---

## Chunk 3: Interactables Mod — Server Side

### Task 13: Interactables mod server — tables, mod, reducers

**Files:**
- Create: `mods/interactables/server/InteractablesTables.cs`
- Create: `mods/interactables/server/InteractablesMod.cs`
- Create: `mods/interactables/server/InteractablesReducers.cs`
- Create: `mods/interactables/server/SmeltConfig.cs`
- Modify: `server/StdbModule.csproj` — include interactables mod files

- [ ] **Step 1: Create `mods/interactables/server/InteractablesTables.cs`**

```csharp
// mods/interactables/server/InteractablesTables.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "furnace_state", Public = true)]
    public partial struct FurnaceState
    {
        [PrimaryKey] public ulong StructureId;
        public string RecipeType;
        public ulong StartTimeMs;
        public ulong DurationMs;
        public bool Complete;
    }

    [Table(Name = "sign_text", Public = true)]
    public partial struct SignText
    {
        [PrimaryKey] public ulong StructureId;
        public string Text;
    }
}
```

- [ ] **Step 2: Create `mods/interactables/server/SmeltConfig.cs`**

```csharp
// mods/interactables/server/SmeltConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>In-memory registry for smelting recipes. Populated during Seed().</summary>
public static class SmeltConfig
{
    public record struct SmeltRecipe(string InputItem, string OutputItem, uint OutputQuantity, ulong DurationMs);

    private static readonly Dictionary<string, SmeltRecipe> _recipes = new();

    public static void Register(string inputItem, string outputItem, uint outputQuantity, ulong durationMs)
        => _recipes[inputItem] = new SmeltRecipe(inputItem, outputItem, outputQuantity, durationMs);

    public static SmeltRecipe? Get(string inputItem)
        => _recipes.TryGetValue(inputItem, out var r) ? r : null;

    public static void Clear() => _recipes.Clear();
}
```

- [ ] **Step 3: Create `mods/interactables/server/InteractablesReducers.cs`**

```csharp
// mods/interactables/server/InteractablesReducers.cs
using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void FurnaceStartSmelt(ReducerContext ctx, ulong structureId)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        // Check no smelt already in progress
        var existing = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (existing is not null)
            throw new Exception("Furnace is already smelting.");

        // Find input slot content
        ContainerSlot? inputSlot = null;
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
            if (cs.ContainerId == structureId && cs.ContainerTable == EntityTables.PlacedStructure && cs.Slot == 0)
            { inputSlot = cs; break; }

        if (inputSlot is null) throw new Exception("Furnace input is empty.");
        var input = inputSlot.Value;

        var recipe = SmeltConfig.Get(input.ItemType);
        if (recipe is null) throw new Exception($"Cannot smelt {input.ItemType}.");

        var r = recipe.Value;
        var now = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds();

        ctx.Db.FurnaceState.Insert(new FurnaceState
        {
            StructureId = structureId,
            RecipeType = input.ItemType,
            StartTimeMs = now,
            DurationMs = r.DurationMs,
            Complete = false,
        });

        // Consume one input item
        if (input.Quantity <= 1)
            ctx.Db.ContainerSlot.Delete(input);
        else
        {
            input.Quantity -= 1;
            ctx.Db.ContainerSlot.Id.Update(input);
        }

        Log.Info($"Furnace {structureId} started smelting {r.InputItem}");
    }

    [Reducer]
    public static void FurnaceCollect(ReducerContext ctx, ulong structureId)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        var state = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (state is null) throw new Exception("Furnace is not smelting.");

        var fs = state.Value;
        var now = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds();

        if (now < fs.StartTimeMs + fs.DurationMs)
            throw new Exception("Smelting not complete yet.");

        var recipe = SmeltConfig.Get(fs.RecipeType);
        if (recipe is null) throw new Exception("Smelt recipe not found.");

        var r = recipe.Value;

        // Place result in output slot (slot 1) or player inventory
        ContainerSlot? outputSlot = null;
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
            if (cs.ContainerId == structureId && cs.ContainerTable == EntityTables.PlacedStructure && cs.Slot == 1)
            { outputSlot = cs; break; }

        if (outputSlot is not null)
        {
            var os = outputSlot.Value;
            if (os.ItemType == r.OutputItem)
            {
                os.Quantity += r.OutputQuantity;
                ctx.Db.ContainerSlot.Id.Update(os);
            }
            else
                throw new Exception("Output slot occupied by different item. Withdraw first.");
        }
        else
        {
            ctx.Db.ContainerSlot.Insert(new ContainerSlot
            {
                ContainerId = structureId,
                ContainerTable = EntityTables.PlacedStructure,
                Slot = 1,
                ItemType = r.OutputItem,
                Quantity = r.OutputQuantity,
            });
        }

        // Clear furnace state
        ctx.Db.FurnaceState.Delete(fs);
        Log.Info($"Furnace {structureId} produced {r.OutputQuantity}x {r.OutputItem}");
    }

    [Reducer]
    public static void FurnaceCancelSmelt(ReducerContext ctx, ulong structureId)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        var state = ctx.Db.FurnaceState.StructureId.Find(structureId);
        if (state is null) throw new Exception("Furnace is not smelting.");

        var fs = state.Value;

        // Return input item to input slot
        ContainerSlot? inputSlot = null;
        foreach (var cs in ctx.Db.ContainerSlot.Iter())
            if (cs.ContainerId == structureId && cs.ContainerTable == EntityTables.PlacedStructure && cs.Slot == 0)
            { inputSlot = cs; break; }

        if (inputSlot is not null)
        {
            var slot = inputSlot.Value;
            slot.Quantity += 1;
            ctx.Db.ContainerSlot.Id.Update(slot);
        }
        else
        {
            ctx.Db.ContainerSlot.Insert(new ContainerSlot
            {
                ContainerId = structureId,
                ContainerTable = EntityTables.PlacedStructure,
                Slot = 0,
                ItemType = fs.RecipeType,
                Quantity = 1,
            });
        }

        ctx.Db.FurnaceState.Delete(fs);
        Log.Info($"Furnace {structureId} smelt cancelled");
    }

    [Reducer]
    public static void UpdateSignText(ReducerContext ctx, ulong structureId, string text)
    {
        if (!AccessControlHelper.CanAccess(ctx, structureId, EntityTables.PlacedStructure))
            throw new Exception("Access denied.");

        // Only owner can edit
        var ac = AccessControlHelper.Find(ctx, structureId, EntityTables.PlacedStructure);
        if (ac is not null && ac.Value.OwnerId != ctx.Sender)
            throw new Exception("Only the owner can edit the sign.");

        if (text.Length > 200) text = text[..200];

        var existing = ctx.Db.SignText.StructureId.Find(structureId);
        if (existing is not null)
        {
            var row = existing.Value;
            row.Text = text;
            ctx.Db.SignText.StructureId.Update(row);
        }
        else
        {
            ctx.Db.SignText.Insert(new SignText
            {
                StructureId = structureId,
                Text = text,
            });
        }
    }
}
```

- [ ] **Step 4: Create `mods/interactables/server/InteractablesMod.cs`**

```csharp
// mods/interactables/server/InteractablesMod.cs
using SpacetimeDB;
using System;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly InteractablesModImpl _interactablesMod = new();

    private sealed class InteractablesModImpl : IMod
    {
        public InteractablesModImpl() => ModLoader.Register(this);

        public string   Name         => "interactables";
        public string   Version      => "1.0.0";
        public string[] Dependencies => new[] { "base" };

        public void Seed(ReducerContext ctx)
        {
            RegisterContainerTypes();
            RegisterSmeltRecipes();
            RegisterStructureHooks();
            SeedCraftingTableRecipes(ctx);
            Log.Info("[InteractablesMod] Seeded.");
        }
    }

    private static void RegisterSmeltRecipes()
    {
        SmeltConfig.Register("raw_iron", "iron", 1, 10_000); // 10 seconds
    }

    private static void RegisterContainerTypes()
    {
        ContainerConfig.Register("chest", 16);
        ContainerConfig.Register("furnace", 2); // slot 0 = input, slot 1 = output
    }

    private static void RegisterStructureHooks()
    {
        // Chest: 16-slot container + access control
        // Note: do NOT pre-create empty ContainerSlot rows — slots are created on first deposit.
        // ContainerConfig is keyed by structure type, not entity ID.
        StructureHooks.RegisterOnPlace("chest", (ctx, s) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = ctx.Sender,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("chest", (ctx, s) =>
        {
            // Collect rows to delete first to avoid delete-during-iteration
            var toDelete = new List<ContainerSlot>();
            foreach (var cs in ctx.Db.ContainerSlot.Iter())
                if (cs.ContainerId == s.Id && cs.ContainerTable == EntityTables.PlacedStructure)
                    toDelete.Add(cs);
            foreach (var cs in toDelete)
            {
                if (!string.IsNullOrEmpty(cs.ItemType) && cs.Quantity > 0)
                {
                    ctx.Db.WorldItem.Insert(new WorldItem
                    {
                        ItemType = cs.ItemType,
                        Quantity = cs.Quantity,
                        PosX = s.PosX, PosY = s.PosY, PosZ = s.PosZ,
                    });
                }
                ctx.Db.ContainerSlot.Delete(cs);
            }
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // Furnace: 2-slot container (input=0, output=1) + access control + FurnaceState cleanup
        StructureHooks.RegisterOnPlace("furnace", (ctx, s) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = ctx.Sender,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("furnace", (ctx, s) =>
        {
            var toDelete = new List<ContainerSlot>();
            foreach (var cs in ctx.Db.ContainerSlot.Iter())
                if (cs.ContainerId == s.Id && cs.ContainerTable == EntityTables.PlacedStructure)
                    toDelete.Add(cs);
            foreach (var cs in toDelete)
            {
                if (!string.IsNullOrEmpty(cs.ItemType) && cs.Quantity > 0)
                {
                    ctx.Db.WorldItem.Insert(new WorldItem
                    {
                        ItemType = cs.ItemType, Quantity = cs.Quantity,
                        PosX = s.PosX, PosY = s.PosY, PosZ = s.PosZ,
                    });
                }
                ctx.Db.ContainerSlot.Delete(cs);
            }
            var fs = ctx.Db.FurnaceState.StructureId.Find(s.Id);
            if (fs is not null) ctx.Db.FurnaceState.Delete(fs.Value);
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // Crafting table: just access control, no container
        StructureHooks.RegisterOnPlace("crafting_table", (ctx, s) =>
        {
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = ctx.Sender,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("crafting_table", (ctx, s) =>
        {
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });

        // Sign: sign text + access control
        StructureHooks.RegisterOnPlace("sign", (ctx, s) =>
        {
            ctx.Db.SignText.Insert(new SignText { StructureId = s.Id, Text = "" });
            ctx.Db.AccessControl.Insert(new AccessControl
            {
                EntityId = s.Id,
                EntityTable = EntityTables.PlacedStructure,
                OwnerId = ctx.Sender,
                IsPublic = true,
            });
        });
        StructureHooks.RegisterOnRemove("sign", (ctx, s) =>
        {
            var st = ctx.Db.SignText.StructureId.Find(s.Id);
            if (st is not null) ctx.Db.SignText.Delete(st.Value);
            var ac = AccessControlHelper.Find(ctx, s.Id, EntityTables.PlacedStructure);
            if (ac is not null) ctx.Db.AccessControl.Delete(ac.Value);
        });
    }

    private static void SeedCraftingTableRecipes(ReducerContext ctx)
    {
        // Example crafting-table-only recipe
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType   = "iron_pickaxe",
            ResultQuantity   = 1,
            Ingredients      = "iron:3,wood:2",
            CraftTimeSeconds = 0,
            Station          = "crafting_table",
        });
    }
}
```

- [ ] **Step 5: Add interactables mod to `server/StdbModule.csproj`**

Add after the hello-world line (line 19):
```xml
    <Compile Include="../mods/interactables/server/**/*.cs" />
```

- [ ] **Step 6: Register new structure types in `BaseMod.RegisterStructures()`**

Add to `mods/base/server/BaseMod.cs` in `RegisterStructures()` after the chest line:
```csharp
        StructureConfig.Register("furnace",        80f);
        StructureConfig.Register("crafting_table", 100f);
        StructureConfig.Register("sign",            30f);
```

- [ ] **Step 7: Build server to verify**

Run: `cd server && spacetime build`
Expected: Build succeeds.

- [ ] **Step 8: Commit**

```bash
git add mods/interactables/server/ server/StdbModule.csproj mods/base/server/BaseMod.cs
git commit -m "feat(server): add interactables mod — chest, furnace, crafting table, sign with structure hooks"
```

---

## Chunk 4: Interactables Mod — Client Side

### Task 14: Interactables client mod — registration and content

**Files:**
- Create: `client/mods/interactables/InteractablesClientMod.cs`
- Create: `client/mods/interactables/content/InteractablesContent.cs`
- Modify: `client/mods/base/content/BaseContent.cs` — add furnace, crafting_table, sign structure defs
- Modify: `client/project.godot` — add autoload

- [ ] **Step 1: Create `client/mods/interactables/InteractablesClientMod.cs`**

```csharp
// client/mods/interactables/InteractablesClientMod.cs
using Godot;

namespace SandboxRPG;

public partial class InteractablesClientMod : Node, IClientMod
{
    public string ModName => "interactables";
    public string[] Dependencies => new[] { "base" };

    public override void _Ready()
    {
        ModManager.Register(this);
    }

    public void Initialize(Node sceneRoot)
    {
        InteractablesContent.RegisterAll();
    }
}
```

- [ ] **Step 2: Create `client/mods/interactables/content/InteractablesContent.cs`**

```csharp
// client/mods/interactables/content/InteractablesContent.cs
using Godot;

namespace SandboxRPG;

public static class InteractablesContent
{
    public static void RegisterAll()
    {
        RegisterItems();
        RegisterStructures();
    }

    private static void RegisterItems()
    {
        ItemRegistry.Register("raw_iron", new ItemDef
        {
            DisplayName = "Raw Iron",
            TintColor = new Color(0.5f, 0.4f, 0.35f),
            MaxStack = 50,
        });
    }

    private static void RegisterStructures()
    {
        StructureRegistry.Register("furnace", new StructureDef
        {
            DisplayName = "Furnace",
            TintColor = new Color(0.7f, 0.3f, 0.2f),
            CollisionSize = new Vector3(1.0f, 1.0f, 1.0f),
            CollisionCenter = new Vector3(0, 0.5f, 0),
            YOffset = 0.5f,
        });

        StructureRegistry.Register("crafting_table", new StructureDef
        {
            DisplayName = "Crafting Table",
            ModelPath = "res://assets/models/survival/workbench.glb",
            TintColor = new Color(0.9f, 0.75f, 0.5f),
            CollisionSize = new Vector3(1.2f, 0.8f, 0.6f),
            CollisionCenter = new Vector3(0, 0.4f, 0),
            YOffset = 0.4f,
        });

        StructureRegistry.Register("sign", new StructureDef
        {
            DisplayName = "Sign",
            TintColor = new Color(0.8f, 0.7f, 0.5f),
            CollisionSize = new Vector3(0.6f, 1.0f, 0.1f),
            CollisionCenter = new Vector3(0, 0.5f, 0),
            YOffset = 0.5f,
        });
    }
}
```

- [ ] **Step 3: Add new item defs for craftable structure items in `BaseContent.RegisterItems()`**

In `client/mods/base/content/BaseContent.cs`, add in `RegisterItems()`:
```csharp
        ItemRegistry.Register("furnace",        new ItemDef { DisplayName = "Furnace",        MaxStack = 1 });
        ItemRegistry.Register("crafting_table", new ItemDef { DisplayName = "Crafting Table", MaxStack = 1 });
        ItemRegistry.Register("sign",           new ItemDef { DisplayName = "Sign",           MaxStack = 10 });
```

- [ ] **Step 4: Add autoload to `client/project.godot`**

Add after the HelloWorldClientMod line:
```ini
InteractablesClientMod="*res://mods/interactables/InteractablesClientMod.cs"
```

- [ ] **Step 5: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add client/mods/interactables/ client/mods/base/content/BaseContent.cs client/project.godot
git commit -m "feat(client): add interactables client mod with content registration"
```

---

### Task 15: Interactables client — UI panels

**Files:**
- Create: `client/mods/interactables/ui/ContainerPanel.cs` (chest UI)
- Create: `client/mods/interactables/ui/FurnacePanel.cs`
- Create: `client/mods/interactables/ui/CraftingTablePanel.cs`
- Create: `client/mods/interactables/ui/SignPanel.cs`

- [ ] **Step 1: Create `client/mods/interactables/ui/ContainerPanel.cs`**

```csharp
// client/mods/interactables/ui/ContainerPanel.cs
using Godot;

namespace SandboxRPG;

/// <summary>Chest UI — inventory left, container grid right.</summary>
public partial class ContainerPanel : InteractionPanel
{
    private ContainerGrid _containerGrid = null!;
    private readonly ulong _containerId;
    private readonly string _containerTable;
    private readonly int _slotCount;
    private readonly string _title;

    public ContainerPanel(ulong containerId, string containerTable, int slotCount, string title = "Chest")
    {
        _containerId = containerId;
        _containerTable = containerTable;
        _slotCount = slotCount;
        _title = title;
    }

    protected override string PanelTitle => _title;

    public override void OnPushed()
    {
        base.OnPushed();
        GameManager.Instance.ContainerSlotChanged += RefreshAll;
    }

    public override void OnPopped()
    {
        GameManager.Instance.ContainerSlotChanged -= RefreshAll;
        base.OnPopped();
    }

    protected override Control BuildContextSide()
    {
        _containerGrid = new ContainerGrid(_containerId, _containerTable, _slotCount, _title);
        return _containerGrid;
    }

    protected override void RefreshContextSide()
    {
        _containerGrid?.Refresh();
    }
}
```

- [ ] **Step 2: Create `client/mods/interactables/ui/FurnacePanel.cs`**

```csharp
// client/mods/interactables/ui/FurnacePanel.cs
using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>Furnace UI — inventory left, input/output slots + progress right.</summary>
public partial class FurnacePanel : InteractionPanel
{
    private readonly ulong _structureId;
    private VBoxContainer _rightSide = null!;
    private ProgressBar? _progressBar;
    private Label? _statusLabel;

    public FurnacePanel(ulong structureId)
    {
        _structureId = structureId;
    }

    protected override string PanelTitle => "Furnace";

    protected override Control BuildContextSide()
    {
        _rightSide = UIFactory.MakeVBox(8);

        _rightSide.AddChild(UIFactory.MakeLabel("Furnace", 14, UIFactory.ColourAccent));

        // Input slot
        _rightSide.AddChild(UIFactory.MakeLabel("Input:", 12, UIFactory.ColourMuted));
        // Will be populated in RefreshContextSide

        // Progress
        _progressBar = new ProgressBar { CustomMinimumSize = new Vector2(200, 24), Value = 0 };
        _rightSide.AddChild(_progressBar);

        _statusLabel = UIFactory.MakeLabel("Idle", 12);
        _rightSide.AddChild(_statusLabel);

        // Output slot
        _rightSide.AddChild(UIFactory.MakeLabel("Output:", 12, UIFactory.ColourMuted));

        // Buttons
        var btnRow = UIFactory.MakeHBox(8);
        _rightSide.AddChild(btnRow);

        var smeltBtn = UIFactory.MakeButton("Smelt", 13, new Vector2(80, 34));
        smeltBtn.Pressed += () => GameManager.Instance.Conn?.Reducers.FurnaceStartSmelt(_structureId);
        btnRow.AddChild(smeltBtn);

        var collectBtn = UIFactory.MakeButton("Collect", 13, new Vector2(80, 34));
        collectBtn.Pressed += () => GameManager.Instance.Conn?.Reducers.FurnaceCollect(_structureId);
        btnRow.AddChild(collectBtn);

        var cancelBtn = UIFactory.MakeButton("Cancel", 13, new Vector2(80, 34));
        cancelBtn.Pressed += () => GameManager.Instance.Conn?.Reducers.FurnaceCancelSmelt(_structureId);
        btnRow.AddChild(cancelBtn);

        return _rightSide;
    }

    public override void _Process(double delta)
    {
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_progressBar == null || _statusLabel == null) return;
        if (GameManager.Instance.Conn == null) return;

        var state = GameManager.Instance.Conn.Db.FurnaceState.StructureId.Find(_structureId);
        if (state is null)
        {
            _progressBar.Value = 0;
            _statusLabel.Text = "Idle";
            return;
        }

        var fs = state.Value;
        if (fs.Complete)
        {
            _progressBar.Value = 100;
            _statusLabel.Text = "Complete! Click Collect.";
            return;
        }

        var now = (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var elapsed = now > fs.StartTimeMs ? now - fs.StartTimeMs : 0;
        var progress = fs.DurationMs > 0 ? (double)elapsed / fs.DurationMs * 100.0 : 0;
        _progressBar.Value = System.Math.Min(progress, 100);

        if (progress >= 100)
            _statusLabel.Text = "Complete! Click Collect.";
        else
            _statusLabel.Text = $"Smelting... {(int)progress}%";
    }

    protected override void RefreshContextSide() { }
}
```

- [ ] **Step 3: Create `client/mods/interactables/ui/CraftingTablePanel.cs`**

```csharp
// client/mods/interactables/ui/CraftingTablePanel.cs
namespace SandboxRPG;

/// <summary>Crafting table UI — same as InventoryCraftingPanel but with station="crafting_table".</summary>
public partial class CraftingTablePanel : InventoryCraftingPanel
{
    protected override string PanelTitle => "Crafting Table";

    public CraftingTablePanel()
    {
        Station = "crafting_table";
    }
}
```

- [ ] **Step 4: Create `client/mods/interactables/ui/SignPanel.cs`**

```csharp
// client/mods/interactables/ui/SignPanel.cs
using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>Sign UI — read-only for non-owners, editable for owners.</summary>
public partial class SignPanel : BasePanel
{
    private readonly ulong _structureId;
    private TextEdit? _textEdit;
    private Label? _readOnlyLabel;

    public SignPanel(ulong structureId)
    {
        _structureId = structureId;
    }

    protected override void BuildUI()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var outerPanel = UIFactory.MakePanel(new Vector2(400, 300));
        center.AddChild(outerPanel);

        var root = UIFactory.MakeVBox(10);
        outerPanel.AddChild(root);

        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(titleRow);
        titleRow.AddChild(UIFactory.MakeTitle("Sign", 20));

        var closeBtn = UIFactory.MakeButton("\u2715", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        root.AddChild(UIFactory.MakeSeparator());

        // Determine if owner
        bool isOwner = false;
        var ac = GameManager.Instance.GetAccessControl(_structureId, "placed_structure");
        if (ac is not null && GameManager.Instance.LocalIdentity is not null)
            isOwner = ac.Value.OwnerId == GameManager.Instance.LocalIdentity.Value;

        // Get current text
        string currentText = "";
        if (GameManager.Instance.Conn != null)
        {
            var st = GameManager.Instance.Conn.Db.SignText.StructureId.Find(_structureId);
            if (st is not null) currentText = st.Value.Text;
        }

        if (isOwner)
        {
            _textEdit = new TextEdit
            {
                Text = currentText,
                CustomMinimumSize = new Vector2(350, 150),
            };
            _textEdit.AddThemeFontSizeOverride("font_size", 14);
            root.AddChild(_textEdit);

            var saveBtn = UIFactory.MakeButton("Save", 14, new Vector2(100, 34));
            saveBtn.Pressed += () =>
            {
                var text = _textEdit.Text;
                GameManager.Instance.Conn?.Reducers.UpdateSignText(_structureId, text);
                UIManager.Instance.Pop();
            };
            root.AddChild(saveBtn);
        }
        else
        {
            _readOnlyLabel = UIFactory.MakeLabel(
                string.IsNullOrEmpty(currentText) ? "(empty)" : currentText, 14);
            _readOnlyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _readOnlyLabel.CustomMinimumSize = new Vector2(350, 150);
            root.AddChild(_readOnlyLabel);
        }
    }
}
```

- [ ] **Step 5: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add client/mods/interactables/ui/
git commit -m "feat(client): add interactable UI panels — ContainerPanel, FurnacePanel, CraftingTablePanel, SignPanel"
```

---

### Task 16: Make structures implement IInteractable via StructureSpawner

**Files:**
- Create: `client/mods/interactables/interaction/InteractableStructure.cs`
- Modify: `client/mods/base/spawners/StructureSpawner.cs` — use InteractableStructure for known types

- [ ] **Step 1: Create `client/mods/interactables/interaction/InteractableStructure.cs`**

```csharp
// client/mods/interactables/interaction/InteractableStructure.cs
using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// IInteractable implementation for placed structures.
/// The structure type determines the interaction behavior.
/// </summary>
public partial class InteractableStructure : StaticBody3D, IInteractable
{
    public ulong StructureId { get; set; }
    public string StructureType { get; set; } = "";
    public string OwnerId { get; set; } = "";

    public string HintText => StructureType switch
    {
        "chest"          => "[E] Open Chest",
        "furnace"        => "[E] Use Furnace",
        "crafting_table" => "[E] Use Crafting Table",
        "sign"           => "[E] Read Sign",
        _                => $"[E] Use {StructureType.Replace('_', ' ')}",
    };

    public string InteractAction => "interact";

    public bool CanInteract(Player? player)
    {
        if (player == null) return false;
        var ac = GameManager.Instance.GetAccessControl(StructureId, "placed_structure");
        if (ac is null) return true; // no access control = allow
        if (ac.Value.IsPublic) return true;
        return ac.Value.OwnerId == player.Identity;
    }

    public void Interact(Player? player)
    {
        switch (StructureType)
        {
            case "chest":
                UIManager.Instance.Push(new ContainerPanel(StructureId, "placed_structure", 16, "Chest"));
                break;
            case "furnace":
                UIManager.Instance.Push(new FurnacePanel(StructureId));
                break;
            case "crafting_table":
                UIManager.Instance.Push(new CraftingTablePanel());
                break;
            case "sign":
                UIManager.Instance.Push(new SignPanel(StructureId));
                break;
        }
    }
}
```

- [ ] **Step 2: Modify `StructureSpawner.CreateStructureVisual` to use `InteractableStructure`**

In `client/mods/base/spawners/StructureSpawner.cs`, change `CreateStructureVisual` to check if the structure type has an interaction (chest, furnace, crafting_table, sign) and use `InteractableStructure` instead of plain `StaticBody3D`.

Replace the `StaticBody3D` creation:

```csharp
    private static Node3D CreateStructureVisual(PlacedStructure s)
    {
        var def  = StructureRegistry.Get(s.StructureType);

        // Use InteractableStructure for types that support interaction
        StaticBody3D body;
        if (IsInteractableType(s.StructureType))
        {
            body = new InteractableStructure
            {
                Name = $"Structure_{s.Id}",
                CollisionLayer = 1,
                CollisionMask = 1,
                StructureId = s.Id,
                StructureType = s.StructureType,
                OwnerId = s.OwnerId.ToString(),
            };
        }
        else
        {
            body = new StaticBody3D { Name = $"Structure_{s.Id}", CollisionLayer = 1, CollisionMask = 1 };
        }

        var visual = ContentSpawner.SpawnVisual(def, s.StructureType);
        body.AddChild(visual);

        var collSize   = def?.CollisionSize   ?? Vector3.One;
        var collCenter = def?.CollisionCenter ?? Vector3.Zero;
        body.AddChild(new CollisionShape3D
        {
            Shape    = new BoxShape3D { Size = collSize },
            Position = collCenter,
        });

        body.Position = new Vector3(s.PosX, s.PosY, s.PosZ);
        body.Rotation = new Vector3(0, s.RotY, 0);
        body.SetMeta("structure_id",   (long)s.Id);
        body.SetMeta("structure_type", s.StructureType);
        body.SetMeta("owner_id",       s.OwnerId.ToString());
        return body;
    }

    private static readonly HashSet<string> _interactableTypes = new()
    {
        "chest", "furnace", "crafting_table", "sign"
    };

    private static bool IsInteractableType(string structureType)
        => _interactableTypes.Contains(structureType);
```

Add `using System.Collections.Generic;` at top if not already present.

- [ ] **Step 3: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add client/mods/interactables/interaction/InteractableStructure.cs \
       client/mods/base/spawners/StructureSpawner.cs
git commit -m "feat(client): make interactable structures implement IInteractable for E-key interaction"
```

---

## Chunk 5: Currency Mod

### Task 17: Currency mod — server side

**Files:**
- Create: `mods/currency/server/CurrencyMod.cs`
- Modify: `server/StdbModule.csproj` — include currency mod

- [ ] **Step 1: Create `mods/currency/server/CurrencyMod.cs`**

```csharp
// mods/currency/server/CurrencyMod.cs
using SpacetimeDB;
using System;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly CurrencyModImpl _currencyMod = new();

    private sealed class CurrencyModImpl : IMod
    {
        public CurrencyModImpl() => ModLoader.Register(this);

        public string   Name         => "currency";
        public string   Version      => "1.0.0";
        public string[] Dependencies => new[] { "base" };

        public void Seed(ReducerContext ctx)
        {
            SeedCurrencyRecipes(ctx);
            Log.Info("[CurrencyMod] Seeded.");
        }
    }

    private static void SeedCurrencyRecipes(ReducerContext ctx)
    {
        // Copper -> Silver
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "silver_coin", ResultQuantity = 1,
            Ingredients = "copper_coin:100", CraftTimeSeconds = 0, Station = "",
        });
        // Silver -> Copper
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "copper_coin", ResultQuantity = 100,
            Ingredients = "silver_coin:1", CraftTimeSeconds = 0, Station = "",
        });
        // Silver -> Gold
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "gold_coin", ResultQuantity = 1,
            Ingredients = "silver_coin:100", CraftTimeSeconds = 0, Station = "",
        });
        // Gold -> Silver
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "silver_coin", ResultQuantity = 100,
            Ingredients = "gold_coin:1", CraftTimeSeconds = 0, Station = "",
        });
        // Gold -> Platinum
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "platinum_coin", ResultQuantity = 1,
            Ingredients = "gold_coin:100", CraftTimeSeconds = 0, Station = "",
        });
        // Platinum -> Gold
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "gold_coin", ResultQuantity = 100,
            Ingredients = "platinum_coin:1", CraftTimeSeconds = 0, Station = "",
        });
    }
}
```

- [ ] **Step 2: Add currency mod to `server/StdbModule.csproj`**

Add after the interactables line:
```xml
    <Compile Include="../mods/currency/server/**/*.cs" />
```

- [ ] **Step 3: Build server to verify**

Run: `cd server && spacetime build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add mods/currency/server/ server/StdbModule.csproj
git commit -m "feat(server): add currency mod — 6 bidirectional coin conversion recipes"
```

---

### Task 18: Currency mod — client side

**Files:**
- Create: `client/mods/currency/CurrencyClientMod.cs`
- Modify: `client/project.godot` — add autoload

- [ ] **Step 1: Create `client/mods/currency/CurrencyClientMod.cs`**

```csharp
// client/mods/currency/CurrencyClientMod.cs
using Godot;

namespace SandboxRPG;

public partial class CurrencyClientMod : Node, IClientMod
{
    public string ModName => "currency";
    public string[] Dependencies => new[] { "base" };

    public override void _Ready()
    {
        ModManager.Register(this);
    }

    public void Initialize(Node sceneRoot)
    {
        RegisterCurrencyItems();
    }

    private static void RegisterCurrencyItems()
    {
        ItemRegistry.Register("copper_coin", new ItemDef
        {
            DisplayName = "Copper Coin",
            TintColor = new Color("#B87333"),
            MaxStack = 100,
        });
        ItemRegistry.Register("silver_coin", new ItemDef
        {
            DisplayName = "Silver Coin",
            TintColor = new Color("#C0C0C0"),
            MaxStack = 100,
        });
        ItemRegistry.Register("gold_coin", new ItemDef
        {
            DisplayName = "Gold Coin",
            TintColor = new Color("#FFD700"),
            MaxStack = 100,
        });
        ItemRegistry.Register("platinum_coin", new ItemDef
        {
            DisplayName = "Platinum Coin",
            TintColor = new Color("#E5E4E2"),
            MaxStack = 100,
        });
    }
}
```

- [ ] **Step 2: Add autoload to `client/project.godot`**

Add after the InteractablesClientMod line:
```ini
CurrencyClientMod="*res://mods/currency/CurrencyClientMod.cs"
```

- [ ] **Step 3: Build client to verify**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add client/mods/currency/ client/project.godot
git commit -m "feat(client): add currency client mod — registers 4 coin ItemDefs"
```

---

## Chunk 6: Final Regeneration, Integration Build & Smoke Test

### Task 19: Final binding regeneration and full build

- [ ] **Step 1: Rebuild server**

Run: `cd server && spacetime build`
Expected: Build succeeds.

- [ ] **Step 2: Regenerate bindings**

Run:
```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 3: Build client**

Run: `cd client && dotnet build SandboxRPG.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit bindings if changed**

```bash
git add client/scripts/networking/SpacetimeDB/
git commit -m "chore: final binding regeneration with all mods"
```

### Task 20: Smoke test with dev environment

- [ ] **Step 1: Start SpacetimeDB server**

Use the `dev-start` skill or manually:
```bash
spacetime start --in-memory
```

- [ ] **Step 2: Login and publish**

```bash
spacetime logout && spacetime login --server-issued-login local --no-browser
cd server && spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 3: Launch Godot and verify**

Open the game. Check:
- Inventory panel (I key) still works
- Crafting recipes show up (including currency conversions)
- Can place a chest, press E on it, see the container panel
- Can place a sign, press E, see text editor
- Station-only recipes are greyed out in normal inventory

- [ ] **Step 4: Final commit if any fixes needed**
