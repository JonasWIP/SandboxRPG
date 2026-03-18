# Currency Mod, Interactables Mod & Engine Infrastructure Design

**Date:** 2026-03-18
**Status:** Approved
**Approach:** B — Engine Containers

---

## Overview

Three interconnected deliverables:

1. **Engine Infrastructure** — Generalized interaction system, split-screen UI framework, access control, and generic container system.
2. **Interactables Mod** — Chest, crafting table, furnace, and sign as sample interactable objects.
3. **Currency Mod** — Copper, Silver, Gold, Platinum coins as stackable items with bidirectional conversion recipes.

---

## 1. Generalized Interaction System

### Problem

`InteractionSystem.cs` has hardcoded `if` chains checking meta tags (`world_item_id`, `world_object` group). Adding new interaction types means editing this core file.

### Solution: `IInteractable` Interface

**Location:** `client/scripts/interaction/IInteractable.cs`

```csharp
public interface IInteractable
{
    string HintText { get; }              // "[E] Open Chest", "[E] Read Sign"
    string InteractAction => "interact";  // input action name (default: E key)
    bool CanInteract(Player player);      // access control check
    void Interact(Player player);         // open UI, trigger action, etc.
}
```

### Refactored InteractionSystem

- Still raycast-based, still checks collision layers.
- Instead of checking meta tags, checks if the collider (or its parent) implements `IInteractable`.
- Priority order: `IInteractable` on Layer 2 (items) > `IInteractable` on Layer 1 (objects/structures).
- Existing behaviors become `IInteractable` implementations:
  - `WorldItemPickup` — wraps current "E to pick up" logic.
  - `HarvestableObject` — wraps current "LMB to harvest" logic.
- New interactables just implement `IInteractable`.

### Dispatch Flow

```
Raycast hit -> find IInteractable on node or ancestors
  -> show HintText label
  -> on input action pressed: call CanInteract() -> Interact()
```

Backward compatible — existing world items and objects keep working through the new interface.

---

## 2. Split-Screen UI Framework

### Problem

`InventoryCraftingPanel` is monolithic. Adding new interaction UIs (chest, furnace) would duplicate the inventory grid code.

### Solution: `InteractionPanel` Base Class

**Location:** `client/scripts/ui/InteractionPanel.cs`

```csharp
public abstract partial class InteractionPanel : BasePanel
{
    protected override void BuildUI()
    {
        // HBoxContainer: left = BuildInventorySide(), right = BuildContextSide()
    }

    protected virtual Control BuildInventorySide()
    {
        // Reusable InventoryGrid component
    }

    protected abstract Control BuildContextSide();  // Mod provides this
}
```

### InventoryGrid Extraction

The inventory grid logic from `InventoryCraftingPanel` is extracted into a reusable `InventoryGrid` component:
- Renders player items in a 4xN grid.
- Right-click context menu (hotbar assign, drop).
- Can be embedded in any panel.

### Refactored Panels

- `InventoryCraftingPanel` becomes an `InteractionPanel` where `BuildContextSide()` returns the recipe list. Opened via I/C keys as before.
- New panels (chest, furnace, crafting table) subclass `InteractionPanel` and provide their own right side.
- Non-inventory panels (sign) extend `BasePanel` directly.

---

## 3. Access Control System

### Server Table

**Location:** Engine-level, `server/AccessControl.cs`

```csharp
[Table(Name = "access_control", Public = true)]
public partial struct AccessControl
{
    [AutoInc][PrimaryKey] public ulong Id;
    public ulong EntityId;        // references PlacedStructure/WorldObject ID
    public string EntityTable;    // "placed_structure" or "world_object"
    public Identity OwnerId;      // who placed/created it
    public bool IsPublic;         // default: true
}
```

### Access Check Helper

`AccessControlHelper.CanAccess(ctx, entityId, entityTable)`:
- No `AccessControl` row exists -> allow (backward compatible).
- `IsPublic == true` -> allow.
- `ctx.Sender == OwnerId` -> allow.
- Otherwise -> deny.

### Reducers

- `ToggleAccessControl(ctx, entityId, entityTable)` — owner toggles public/private.
- Access check called by interactable reducers before executing actions.

### Client-Side

- `IInteractable.CanInteract()` reads local `AccessControl` table replica.
- Owner sees lock/unlock icon on their objects.
- Denied interaction shows brief "This is private" message.

---

## 4. Generic Container System

### Server Table

**Location:** Engine-level, `server/ContainerSystem.cs`

```csharp
[Table(Name = "container_slot", Public = true)]
public partial struct ContainerSlot
{
    [AutoInc][PrimaryKey] public ulong Id;
    public ulong ContainerId;     // references PlacedStructure or WorldObject ID
    public string ContainerTable; // "placed_structure" or "world_object"
    public int Slot;              // 0-based slot index
    public string ItemType;
    public uint Quantity;
}
```

### Container Config (Runtime Registry)

```csharp
ContainerConfig.Register("chest", slotCount: 16);
ContainerConfig.Register("furnace_input", slotCount: 1);
ContainerConfig.Register("furnace_output", slotCount: 1);
```

Mods register container types during `Seed()`.

### Engine Reducers

- `ContainerDeposit(ctx, containerId, containerTable, inventoryItemId, toSlot)` — player inventory -> container slot.
- `ContainerWithdraw(ctx, containerId, containerTable, fromSlot)` — container -> player inventory.
- `ContainerTransfer(ctx, containerId, containerTable, fromSlot, toSlot)` — move within container.

All three check access control before executing.

### Client: `ContainerGrid` Component

Reusable UI component:
- Renders container slots in a grid.
- Click to withdraw/deposit.
- Shows item type + quantity per slot.
- Listens to `ContainerSlot` table updates for real-time sync.

### Interaction Flow

```
Player presses E on chest
  -> IInteractable.Interact()
  -> UIManager.Push(new ContainerPanel(containerId))
  -> ContainerPanel: inventory left, ContainerGrid right
  -> Click item in inventory -> ContainerDeposit reducer
  -> Click item in container -> ContainerWithdraw reducer
```

---

## 5. Interactables Mod

**Location:** `client/mods/interactables/`, `mods/interactables/server/`
**Depends on:** "base"

### Chest

- **Server:** Registers as container with 16 slots. On placement, creates `ContainerSlot` rows + `AccessControl` row (public by default).
- **Client:** Implements `IInteractable` -> hint "[E] Open Chest". `Interact()` pushes `ContainerPanel`. Owner sees lock/unlock toggle.

### Crafting Table

- **Server:** Registers bonus recipes (Station = "crafting_table"). `CraftingRecipe` table gets a new `string Station` field (empty = anywhere, "crafting_table" = only at station).
- **Client:** Implements `IInteractable` -> hint "[E] Use Crafting Table". `Interact()` pushes an `InteractionPanel` showing all recipes including table-only ones.

### Furnace

- **Server:** Two containers (input: 1 slot, output: 1 slot). `FurnaceState` table:
  ```csharp
  [Table(Name = "furnace_state", Public = true)]
  public partial struct FurnaceState
  {
      [PrimaryKey] public ulong StructureId;
      public string RecipeType;       // what's being smelted
      public ulong StartTimeMs;       // when processing started
      public ulong DurationMs;        // how long it takes
      public bool Complete;
  }
  ```
- **Reducers:** `FurnaceStartSmelt(ctx, structureId)` — checks input, sets timer. `FurnaceCollect(ctx, structureId)` — if complete, moves result to output/inventory.
- **Client:** `FurnacePanel` — inventory left, right shows input slot, output slot, progress bar. Progress from `StartTimeMs + DurationMs` vs current time.
- **Smelt recipes:** Registered by mod (e.g. "raw_iron" -> "iron_ingot", 10s). Separate from crafting recipes.

### Sign

- **Server:** `SignText` table:
  ```csharp
  [Table(Name = "sign_text", Public = true)]
  public partial struct SignText
  {
      [PrimaryKey] public ulong StructureId;
      public string Text;
  }
  ```
- **Reducer:** `UpdateSignText(ctx, structureId, text)` — access-controlled, max 200 chars.
- **Client:** Implements `IInteractable` -> hint "[E] Read Sign". `SignPanel` extends `BasePanel` directly (no inventory side). Shows text; owner gets editable field + save. `Label3D` above sign shows first line.

---

## 6. Currency Mod

**Location:** `client/mods/currency/`, `mods/currency/server/`
**Depends on:** "base"

### Items

| Item | MaxStack | Display Name |
|------|----------|-------------|
| `copper_coin` | 100 | Copper Coin |
| `silver_coin` | 100 | Silver Coin |
| `gold_coin` | 100 | Gold Coin |
| `platinum_coin` | 100 | Platinum Coin |

### Conversion Recipes

Registered as `CraftingRecipe` rows (Station = "" — craftable anywhere):

| Ingredients | Result |
|-------------|--------|
| copper_coin:100 | silver_coin x1 |
| silver_coin:1 | copper_coin x100 |
| silver_coin:100 | gold_coin x1 |
| gold_coin:1 | silver_coin x100 |
| gold_coin:100 | platinum_coin x1 |
| platinum_coin:1 | gold_coin x100 |

### Server Mod

`CurrencyMod : IMod` — `Seed()` inserts 6 crafting recipes. No custom tables or reducers. Pure data mod.

### Client Mod

`CurrencyClientMod : IClientMod` — `Initialize()` registers 4 `ItemDef` entries. No custom UI — conversions appear in regular crafting panel.

---

## File Layout Summary

### Engine (core infrastructure)

```
client/scripts/interaction/
  IInteractable.cs              # interface

client/scripts/ui/
  InteractionPanel.cs           # split-screen base class
  InventoryGrid.cs              # extracted reusable inventory grid
  ContainerGrid.cs              # reusable container slot grid

server/
  AccessControl.cs              # table + helper + reducer
  ContainerSystem.cs            # table + config + reducers
```

### Interactables Mod

```
client/mods/interactables/
  InteractablesClientMod.cs     # autoload, registers content
  content/InteractablesContent.cs
  ui/ContainerPanel.cs          # chest UI
  ui/FurnacePanel.cs            # furnace UI
  ui/CraftingTablePanel.cs      # crafting table UI
  ui/SignPanel.cs               # sign UI

mods/interactables/server/
  InteractablesMod.cs           # IMod, seeds config
  InteractablesTables.cs        # FurnaceState, SignText tables
  InteractablesReducers.cs      # FurnaceStartSmelt, FurnaceCollect, UpdateSignText
```

### Currency Mod

```
client/mods/currency/
  CurrencyClientMod.cs          # autoload, registers ItemDefs

mods/currency/server/
  CurrencyMod.cs                # IMod, seeds recipes
```

### Refactored Base Mod Files

```
client/mods/base/world/InteractionSystem.cs  # refactored to use IInteractable
client/mods/base/ui/InventoryCraftingPanel.cs # refactored to extend InteractionPanel
```
