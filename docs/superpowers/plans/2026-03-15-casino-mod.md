# Casino Mod Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a mod framework, Copper/Silver/Gold currency system, and a casino pack (slot machine, multiplayer blackjack, coin pusher, reaction + pattern arcade machines) as isolated, toggleable mods.

**Architecture:** Mods live in `server/mods/<name>/` and `client/mods/<name>/` folders gated by C# compile symbols (`MOD_CURRENCY`, `MOD_CASINO`). A `ModConfig` DB table handles runtime toggling. All casino game logic is server-authoritative; clients render state directly on machine surfaces via SubViewport textures and 3D objects — no full-screen panels.

**Tech Stack:** SpacetimeDB 2.0 C# WASM server · Godot 4.6.1 C# client · SpacetimeDB.ClientSDK 2.0.1 · .NET 8.0

**Spec:** `docs/superpowers/specs/2026-03-15-casino-mod-design.md`

---

## File Map

### Created (server)
- `server/mods/currency/mod.json` — manifest
- `server/mods/currency/Tables.cs` — CurrencyBalance, CurrencyTransaction
- `server/mods/currency/Reducers.cs` — ExchangeResources, WithdrawCoins, DepositCoins + internal CreditCoins/DebitCoins helpers
- `server/mods/currency/Lifecycle.cs` — GrantStartingBalance (internal helper)
- `server/mods/casino/mod.json` — manifest
- `server/mods/casino/Tables.cs` — SlotSession, BlackjackGame, BlackjackSeat, CoinPusherState, ArcadeSession
- `server/mods/casino/Reducers.cs` — all casino game reducers
- `server/mods/casino/Lifecycle.cs` — seed casino POI building + machines + recipes

### Created (client)
- `client/scripts/networking/ModManager.cs` — singleton autoload; reads ModConfig, registers mod UI and callbacks
- `client/mods/currency/mod.json`
- `client/mods/currency/CurrencyHUD.cs` — top-right Copper/Silver/Gold overlay
- `client/mods/currency/ExchangeUI.cs` — resource exchange + coin withdraw/deposit UI (small popup)
- `client/mods/casino/mod.json`
- `client/mods/casino/CasinoUI.cs` — routes E-press to correct machine UI
- `client/mods/casino/SlotMachineUI.cs` — SubViewport reel display + bet popup
- `client/mods/casino/BlackjackUI.cs` — seat join prompt + 3D card mesh spawner
- `client/mods/casino/CoinPusherUI.cs` — RigidBody3D coin spawner on CoinCount change
- `client/mods/casino/ArcadeUI.cs` — reaction needle + pattern button animations on screen

### Modified
- `server/Tables.cs` — add ModConfig, AdminList tables
- `server/Lifecycle.cs` — seed ModConfig; call GrantStartingBalance in ClientConnected; add GrantAdmin + SetModEnabled reducers
- `server/StdbModule.csproj` — add `<DefineConstants>MOD_CURRENCY;MOD_CASINO</DefineConstants>`
- `client/SandboxRPG.csproj` — add `<DefineConstants>MOD_CURRENCY;MOD_CASINO</DefineConstants>`
- `client/scripts/world/InteractionSystem.cs` — add static structure handler registry + Area3D raycast leg
- `client/scripts/world/WorldManager.cs` — attach Area3D + CollisionShape3D to casino structure nodes on spawn
- `client/project.godot` — add ModManager autoload entry
- `client/scripts/networking/SpacetimeDB/` — regenerated after each schema change (DO NOT hand-edit)

---

## Chunk 1: Mod Framework

### Task 1.1 — Add ModConfig and AdminList tables to server

**Files:**
- Modify: `server/Tables.cs`

- [ ] **Open `server/Tables.cs`. The file uses `namespace SandboxRPG.Server;` and all structs are inside `public static partial class Module { }`. Append the two new table structs inside the class, before the closing `}`:**

```csharp
    // --- Mod Framework (always compiled, no #if guard) ---

    [Table(Name = "mod_config", Public = true)]
    public partial struct ModConfig
    {
        [PrimaryKey]
        public string ModId;
        public bool Enabled;
        public string Version;
        public string Dependencies; // comma-separated mod IDs
    }

    [Table(Name = "admin_list", Public = true)]
    public partial struct AdminList
    {
        [PrimaryKey]
        public Identity PlayerId;
    }
```

- [ ] **Build server to verify compilation:**
```bash
cd server && spacetime build
```
Expected: Build succeeded, 0 errors.

---

### Task 1.2 — Add GrantAdmin and SetModEnabled reducers

**Files:**
- Modify: `server/Lifecycle.cs`

- [ ] **Append these reducers to `server/Lifecycle.cs` inside `public static partial class Module`:**

```csharp
/// <summary>
/// First-run bootstrap. Only succeeds when AdminList is empty.
/// Call once to make yourself admin, then others via SetModEnabled.
/// </summary>
[Reducer]
public static void GrantAdmin(ReducerContext ctx, Identity target)
{
    if (ctx.Db.AdminList.Iter().Any())
        throw new Exception("AdminList already populated. Use an existing admin.");
    ctx.Db.AdminList.Insert(new AdminList { PlayerId = target });
}

[Reducer]
public static void SetModEnabled(ReducerContext ctx, string modId, bool enabled)
{
    // Auth check
    if (ctx.Db.AdminList.PlayerId.Find(ctx.Sender) == null)
        throw new Exception("Not authorized");

    var row = ctx.Db.ModConfig.ModId.Find(modId)
        ?? throw new Exception($"Unknown mod: {modId}");

    if (enabled)
    {
        // Validate all dependencies are enabled first
        foreach (var dep in row.Dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var depRow = ctx.Db.ModConfig.ModId.Find(dep);
            if (depRow == null || !depRow.Value.Enabled)
                throw new Exception($"Dependency '{dep}' must be enabled first");
        }
    }
    else
    {
        // Validate no enabled mods depend on this one
        foreach (var other in ctx.Db.ModConfig.Iter())
        {
            if (!other.Enabled) continue;
            var deps = other.Dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (deps.Contains(modId))
                throw new Exception($"Mod '{other.ModId}' depends on '{modId}'. Disable it first.");
        }
    }

    ctx.Db.ModConfig.Delete(row.Value);
    ctx.Db.ModConfig.Insert(new ModConfig
    {
        ModId = row.Value.ModId,
        Enabled = enabled,
        Version = row.Value.Version,
        Dependencies = row.Value.Dependencies
    });
}
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 1.3 — Seed ModConfig rows in Init()

**Files:**
- Modify: `server/Lifecycle.cs`

- [ ] **Inside the existing `Init()` reducer, after the existing seed calls, add:**

```csharp
// Seed mod config rows (idempotent — only insert if missing)
if (ctx.Db.ModConfig.ModId.Find("currency") == null)
    ctx.Db.ModConfig.Insert(new ModConfig { ModId = "currency", Enabled = true, Version = "1.0.0", Dependencies = "" });
if (ctx.Db.ModConfig.ModId.Find("casino") == null)
    ctx.Db.ModConfig.Insert(new ModConfig { ModId = "casino", Enabled = true, Version = "1.0.0", Dependencies = "currency" });
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 1.4 — Add compile symbols to both .csproj files

**Files:**
- Modify: `server/StdbModule.csproj`
- Modify: `client/SandboxRPG.csproj`

- [ ] **In `server/StdbModule.csproj`, find the first `<PropertyGroup>` and add inside it:**
```xml
<DefineConstants>$(DefineConstants);MOD_CURRENCY;MOD_CASINO</DefineConstants>
```

- [ ] **In `client/SandboxRPG.csproj`, find the first `<PropertyGroup>` and add inside it:**
```xml
<DefineConstants>$(DefineConstants);MOD_CURRENCY;MOD_CASINO</DefineConstants>
```

> `$(DefineConstants)` preserves existing symbols like `DEBUG` and `TRACE` injected by the .NET SDK.

- [ ] **Build both to verify:**
```bash
cd server && spacetime build
cd ../client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors each.

---

### Task 1.5 — Regenerate client bindings

- [ ] **Build, publish, and regenerate:**
```bash
cd server
spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Rebuild client to confirm generated bindings compile:**
```bash
cd ../client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 1.6 — Create ModManager client singleton

**Files:**
- Create: `client/scripts/networking/ModManager.cs`

- [ ] **Create `client/scripts/networking/ModManager.cs`:**

```csharp
using Godot;
using System.Collections.Generic;
using System.Linq;
using SpacetimeDB.Types;

/// <summary>
/// Singleton autoload. Reads ModConfig table on connect, builds dependency graph,
/// and provides enable/disable state to mod UI scripts.
/// </summary>
public partial class ModManager : Node
{
    public static ModManager Instance { get; private set; }

    // Registered mods in topological dependency order
    private readonly List<string> _enabledMods = new();

    public override void _Ready()
    {
        Instance = this;
        GameManager.Instance.SubscriptionApplied += OnSubscriptionApplied;
    }

    private void OnSubscriptionApplied()
    {
        _enabledMods.Clear();
        var rows = GameManager.Instance.Conn?.Db.ModConfig.Iter().ToList() ?? new();
        var enabled = rows.Where(r => r.Enabled).ToDictionary(r => r.ModId);
        // Topological sort: simple pass until stable
        var sorted = TopologicalSort(enabled);
        _enabledMods.AddRange(sorted);
        GD.Print($"[ModManager] Active mods: {string.Join(", ", _enabledMods)}");
    }

    public bool IsEnabled(string modId) => _enabledMods.Contains(modId);

    private static List<string> TopologicalSort(Dictionary<string, ModConfig> mods)
    {
        var result = new List<string>();
        var visited = new HashSet<string>();

        void Visit(string id)
        {
            if (visited.Contains(id) || !mods.ContainsKey(id)) return;
            visited.Add(id);
            var deps = mods[id].Dependencies
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var dep in deps) Visit(dep);
            result.Add(id);
        }

        foreach (var id in mods.Keys) Visit(id);
        return result;
    }
}
```

- [ ] **Register as autoload in `client/project.godot`:**

Find the `[autoload]` section and add:
```
ModManager="*res://scripts/networking/ModManager.cs"
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 1.7 — Extend InteractionSystem with structure handler registry

**Files:**
- Modify: `client/scripts/world/InteractionSystem.cs`

The existing file (`namespace SandboxRPG;`) has three checks: world item proximity, then world object raycast (trees/rocks). We **add** a structure raycast check between them — no existing behaviour is removed.

- [ ] **Add `using System;` and `using System.Collections.Generic;` to the top of `InteractionSystem.cs` (after existing usings).**

- [ ] **Add the static registry fields and `RegisterStructureHandler` method inside the class, after the existing private fields:**

```csharp
    // ── Structure handler registry (casino mod registers here) ───────────────
    private static readonly Dictionary<string, Action<ulong>> _structureHandlers = new();

    public static void RegisterStructureHandler(string structureType, Action<ulong> handler)
        => _structureHandlers[structureType] = handler;
```

- [ ] **In `_Process`, insert a call to `CheckStructureRaycast` between the existing two checks:**

Find this existing block:
```csharp
        // World items (proximity) take priority
        if (CheckNearbyWorldItems()) return;

        // Fall back to raycast for world objects (trees, rocks, bushes)
        CheckWorldObjectRaycast(spaceState, from, to);
```

Replace with:
```csharp
        // World items (proximity) take priority
        if (CheckNearbyWorldItems()) return;

        // Structure interaction (casino machines via Area3D raycast)
        if (CheckStructureRaycast(spaceState, from, to)) return;

        // Fall back to raycast for world objects (trees, rocks, bushes)
        CheckWorldObjectRaycast(spaceState, from, to);
```

- [ ] **Add the `CheckStructureRaycast` method after `CheckWorldObjectRaycast`:**

```csharp
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
```

> The Area3D on casino structure nodes uses **collision layer 2**. This is set in Task 3.4 when WorldManager spawns casino structures. The existing world object raycast uses the default layer (no mask set), so there is no collision between the two checks.

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 1.8 — Commit chunk 1

- [ ] **Commit:**
```bash
cd C:/Users/Jonas/Documents/GodotGame
git add server/Tables.cs server/Lifecycle.cs server/StdbModule.csproj \
        client/SandboxRPG.csproj client/project.godot \
        client/scripts/networking/ModManager.cs \
        client/scripts/world/InteractionSystem.cs \
        client/scripts/networking/SpacetimeDB/
git commit -m "feat: mod framework — ModConfig table, ModManager singleton, structure interaction registry"
```

---

## Chunk 2: Currency System

### Task 2.1 — Create currency server files

**Files:**
- Create: `server/mods/currency/mod.json`
- Create: `server/mods/currency/Tables.cs`
- Create: `server/mods/currency/Reducers.cs`
- Create: `server/mods/currency/Lifecycle.cs`

- [ ] **Create `server/mods/currency/mod.json`:**
```json
{
  "id": "currency",
  "version": "1.0.0",
  "displayName": "Currency System",
  "dependencies": []
}
```

- [ ] **Create `server/mods/currency/Tables.cs`:**
```csharp
#if MOD_CURRENCY
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "currency_balance", Public = true)]
    public partial struct CurrencyBalance
    {
        [PrimaryKey]
        public Identity PlayerId;
        public ulong Copper; // Silver=Copper/100, Gold=Copper/10000 (display only)
    }

    [Table(Name = "currency_transaction", Public = true)]
    public partial struct CurrencyTransaction
    {
        [PrimaryKey, AutoInc]
        public ulong Id;
        public Identity PlayerId;
        public long Amount;   // positive=credit, negative=debit
        public string Reason;
        public ulong Timestamp;
    }
}
#endif
```

- [ ] **Create `server/mods/currency/Reducers.cs`:**
```csharp
#if MOD_CURRENCY
using SpacetimeDB;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG.Server;

public static partial class Module
{
    // ── Internal helpers (not [Reducer] — not callable by clients) ────────────

    internal static void CreditCoins(ReducerContext ctx, Identity playerId, ulong amount, string reason)
    {
        var existing = ctx.Db.CurrencyBalance.PlayerId.Find(playerId);
        if (existing == null)
        {
            ctx.Db.CurrencyBalance.Insert(new CurrencyBalance { PlayerId = playerId, Copper = amount });
        }
        else
        {
            ctx.Db.CurrencyBalance.Delete(existing.Value);
            ctx.Db.CurrencyBalance.Insert(new CurrencyBalance { PlayerId = playerId, Copper = existing.Value.Copper + amount });
        }
        ctx.Db.CurrencyTransaction.Insert(new CurrencyTransaction
        {
            PlayerId = playerId,
            Amount = (long)amount,
            Reason = reason,
            Timestamp = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000
        });
    }

    internal static void DebitCoins(ReducerContext ctx, Identity playerId, ulong amount, string reason)
    {
        var existing = ctx.Db.CurrencyBalance.PlayerId.Find(playerId)
            ?? throw new Exception("No currency balance found");
        if (existing.Value.Copper < amount)
            throw new Exception($"Insufficient funds: have {existing.Value.Copper}, need {amount}");
        ctx.Db.CurrencyBalance.Delete(existing.Value);
        ctx.Db.CurrencyBalance.Insert(new CurrencyBalance { PlayerId = playerId, Copper = existing.Value.Copper - amount });
        ctx.Db.CurrencyTransaction.Insert(new CurrencyTransaction
        {
            PlayerId = playerId,
            Amount = -(long)amount,
            Reason = reason,
            Timestamp = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000
        });
    }

    // ── Public reducers ───────────────────────────────────────────────────────

    /// <summary>Exchange raw resources for Copper. qty must be a multiple of batch size.</summary>
    [Reducer]
    public static void ExchangeResources(ReducerContext ctx, string resourceType, uint qty)
    {
        // Batch rates: (batchSize, copperYield)
        var rates = new Dictionary<string, (uint batch, uint yield)>
        {
            ["wood"]  = (10, 5),
            ["stone"] = (5,  5),
            ["iron"]  = (1,  20),
        };
        if (!rates.TryGetValue(resourceType, out var rate))
            throw new Exception($"Resource '{resourceType}' has no exchange rate");
        if (qty % rate.batch != 0)
            throw new Exception($"qty must be a multiple of {rate.batch} for {resourceType}");

        uint batches = qty / rate.batch;
        ulong payout = (ulong)(batches * rate.yield);

        // Consume from inventory
        var items = ctx.Db.InventoryItem.Iter().Where(i => i.OwnerId == ctx.Sender);
        uint remaining = qty;
        foreach (var item in items)
        {
            if (item.ItemType != resourceType) continue;
            if (item.Quantity <= remaining)
            {
                remaining -= item.Quantity;
                ctx.Db.InventoryItem.Delete(item);
            }
            else
            {
                var updated = item;
                updated.Quantity -= remaining;
                remaining = 0;
                ctx.Db.InventoryItem.Delete(item);
                ctx.Db.InventoryItem.Insert(updated);
            }
            if (remaining == 0) break;
        }
        if (remaining > 0) throw new Exception($"Not enough {resourceType} in inventory");

        CreditCoins(ctx, ctx.Sender, payout, $"exchange:{resourceType}x{qty}");
    }

    /// <summary>Move Copper from wallet balance to physical coin inventory items.</summary>
    [Reducer]
    public static void WithdrawCoins(ReducerContext ctx, string denomination, uint amount)
    {
        ulong costPerUnit = denomination switch
        {
            "copper" => 1,
            "silver" => 100,
            "gold"   => 10000,
            _ => throw new Exception($"Unknown denomination: {denomination}")
        };
        string itemType = $"coin_{denomination}";
        ulong totalCost = costPerUnit * amount;

        DebitCoins(ctx, ctx.Sender, totalCost, $"withdraw:{denomination}x{amount}");

        // Grant coin items
        var existing = ctx.Db.InventoryItem.Iter().Where(i => i.OwnerId == ctx.Sender)
            .FirstOrDefault(i => i.ItemType == itemType && i.Slot == -1);
        if (existing.ItemType == itemType) // found (struct default check)
        {
            ctx.Db.InventoryItem.Delete(existing);
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender, ItemType = itemType,
                Quantity = existing.Quantity + amount, Slot = -1
            });
        }
        else
        {
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender, ItemType = itemType, Quantity = amount, Slot = -1
            });
        }
    }

    /// <summary>Convert physical coin inventory items back to wallet Copper balance.</summary>
    [Reducer]
    public static void DepositCoins(ReducerContext ctx, string denomination, uint amount)
    {
        ulong valuePerUnit = denomination switch
        {
            "copper" => 1,
            "silver" => 100,
            "gold"   => 10000,
            _ => throw new Exception($"Unknown denomination: {denomination}")
        };
        string itemType = $"coin_{denomination}";
        ulong totalValue = valuePerUnit * amount;

        // Consume coin items from inventory
        var existing = ctx.Db.InventoryItem.Iter().Where(i => i.OwnerId == ctx.Sender)
            .FirstOrDefault(i => i.ItemType == itemType);
        if (existing.ItemType != itemType || existing.Quantity < amount)
            throw new Exception($"Not enough {itemType} in inventory");

        ctx.Db.InventoryItem.Delete(existing);
        if (existing.Quantity > amount)
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender, ItemType = itemType,
                Quantity = existing.Quantity - amount, Slot = existing.Slot
            });

        CreditCoins(ctx, ctx.Sender, totalValue, $"deposit:{denomination}x{amount}");
    }
}
#endif
```

- [ ] **Create `server/mods/currency/Lifecycle.cs`:**
```csharp
#if MOD_CURRENCY
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    /// <summary>
    /// Internal — call from ClientConnected. Idempotent: only creates row if missing.
    /// Awards 500 Copper starting balance on first connect.
    /// </summary>
    internal static void GrantStartingBalance(ReducerContext ctx, Identity identity)
    {
        if (ctx.Db.CurrencyBalance.PlayerId.Find(identity) != null) return;
        CreditCoins(ctx, identity, 500, "starting_balance");
    }
}
#endif
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 2.2 — Hook GrantStartingBalance into ClientConnected

**Files:**
- Modify: `server/Lifecycle.cs`

- [ ] **In the `ClientConnected` reducer, after the existing player creation/online logic, add:**
```csharp
#if MOD_CURRENCY
        GrantStartingBalance(ctx, ctx.Sender);
#endif
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 2.3 — Regenerate and rebuild client

- [ ] **Build, publish, and regenerate bindings:**
```bash
cd server
spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
cd ../client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 2.4 — Create CurrencyHUD client script

**Files:**
- Create: `client/mods/currency/mod.json`
- Create: `client/mods/currency/CurrencyHUD.cs`

- [ ] **Create `client/mods/currency/mod.json`:**
```json
{ "id": "currency", "version": "1.0.0", "dependencies": [] }
```

- [ ] **Create `client/mods/currency/CurrencyHUD.cs`:**
```csharp
#if MOD_CURRENCY
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Small top-right overlay showing Copper / Silver / Gold balance.
/// Added as a child of HUD by ModManager when currency mod is enabled.
/// </summary>
public partial class CurrencyHUD : Control
{
    private Label _label;

    public override void _Ready()
    {
        AnchorRight = 1; AnchorTop = 0;
        OffsetLeft = -220; OffsetRight = -10; OffsetTop = 10; OffsetBottom = 40;

        _label = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        AddChild(_label);

        GameManager.Instance.SubscriptionApplied += Refresh;
        // Subscribe to CurrencyBalance table changes
        GameManager.Instance.Conn.Db.CurrencyBalance.OnInsert += (_, _) => CallDeferred("Refresh");
        GameManager.Instance.Conn.Db.CurrencyBalance.OnUpdate += (_, _, _) => CallDeferred("Refresh");
    }

    public void Refresh()
    {
        var player = GameManager.Instance.GetLocalPlayer();
        if (player == null) return;
        var bal = GameManager.Instance.Conn.Db.CurrencyBalance
            .PlayerId.Find(player.Identity);
        if (bal == null) { _label.Text = ""; return; }
        ulong copper = bal.Value.Copper;
        ulong silver = copper / 100;
        ulong gold   = copper / 10000;
        _label.Text = $"🟤 {copper % 100}  ⚪ {silver % 100}  🟡 {gold}";
    }
}
#endif
```

- [ ] **Register CurrencyHUD in ModManager `OnSubscriptionApplied`, after the sorted list is built:**

In `client/scripts/networking/ModManager.cs`, add at end of `OnSubscriptionApplied`:
```csharp
#if MOD_CURRENCY
        if (IsEnabled("currency"))
        {
            var hud = GetNode<Node>("/root/Main/HUD"); // adjust path if needed
            if (hud != null && hud.FindChild("CurrencyHUD") == null)
            {
                var currencyHud = new CurrencyHUD();
                currencyHud.Name = "CurrencyHUD";
                hud.AddChild(currencyHud);
            }
        }
#endif
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

> **Smoke test:** Start SpacetimeDB, publish module, open Godot, connect. Top-right HUD should show `🟤 500  ⚪ 5  🟡 0` on first connect.

---

### Task 2.5 — Commit chunk 2

- [ ] **Commit:**
```bash
cd C:/Users/Jonas/Documents/GodotGame
git add server/mods/currency/ server/Lifecycle.cs \
        client/mods/currency/ client/scripts/networking/ModManager.cs \
        client/scripts/networking/SpacetimeDB/
git commit -m "feat: currency mod — CurrencyBalance, CreditCoins/DebitCoins, ExchangeResources, WithdrawCoins, DepositCoins, CurrencyHUD"
```

---

## Chunk 3: Casino World Setup + Exchange Machine

### Task 3.1 — Create all casino server tables

**Files:**
- Create: `server/mods/casino/mod.json`
- Create: `server/mods/casino/Tables.cs`

- [ ] **Create `server/mods/casino/mod.json`:**
```json
{
  "id": "casino",
  "version": "1.0.0",
  "displayName": "Casino Pack",
  "dependencies": ["currency"]
}
```

- [ ] **Create `server/mods/casino/Tables.cs`:**
```csharp
#if MOD_CASINO
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{

// ── Slot Machine ──────────────────────────────────────────────────────────────
[Table(Name = "slot_session", Public = true)]
public partial struct SlotSession
{
    [PrimaryKey]
    public ulong MachineId;    // = PlacedStructure.Id
    public Identity PlayerId;  // zero-value = unoccupied
    public string Reels;       // "🍒|🍋|⭐" — result after spin
    public bool IsIdle;        // true = available; false = showing result
    public ulong Bet;
    public ulong WinAmount;
    public ulong ExpiresAt;    // microseconds; 0 = no expiry
}

// ── Blackjack ─────────────────────────────────────────────────────────────────
[Table(Name = "blackjack_game", Public = true)]
public partial struct BlackjackGame
{
    [PrimaryKey]
    public ulong MachineId;
    public byte State;     // 0=WaitingForPlayers 1=PlayerTurns 2=DealerTurn 3=Payout
    public string DealerHand;       // "AS,10H" — full hand (shown after DealerTurn)
    public string DealerHandHidden; // "??,10H" — shown during PlayerTurns
    public string Deck;             // comma-separated remaining draw pile
    public uint RoundId;
}

[Table(Name = "blackjack_seat", Public = true)]
public partial struct BlackjackSeat
{
    [PrimaryKey, AutoInc]
    public ulong Id;
    public ulong MachineId;
    public byte SeatIndex;   // 0–3
    public Identity PlayerId;
    public string Hand;      // "7D,KS"
    public ulong Bet;
    public byte State;       // 0=Waiting 1=Acting 2=Standing 3=Bust 4=Done
    public uint RoundId;     // matches BlackjackGame.RoundId
}

// ── Coin Pusher ───────────────────────────────────────────────────────────────
[Table(Name = "coin_pusher_state", Public = true)]
public partial struct CoinPusherState
{
    [PrimaryKey]
    public ulong MachineId;
    public uint CoinCount;
    public ulong CopperPool;       // total Copper bet since last jackpot reset
    public Identity LastPusherId;  // zero-value = none
    public ulong LastPushTime;
    public uint JackpotThreshold;  // default 200
}

// ── Arcade ────────────────────────────────────────────────────────────────────
[Table(Name = "arcade_session", Public = true)]
public partial struct ArcadeSession
{
    [PrimaryKey]
    public ulong MachineId;
    public Identity PlayerId;      // zero-value = unoccupied
    public byte GameType;          // 0=Reaction 1=Pattern
    public byte State;             // 0=Idle 1=Active 2=Judging
    public ulong Bet;
    public string ChallengeData;   // Reaction: "targetMs:windowMs"; Pattern: "RRBLG"
    public ulong StartTime;        // server microsecond timestamp
    public ulong ExpiresAt;        // microseconds; piggybacked cleanup
}

} // end partial class Module
#endif
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 3.2 — Create casino Lifecycle (seed POI + machines + recipes)

**Files:**
- Create: `server/mods/casino/Lifecycle.cs`

- [ ] **Create `server/mods/casino/Lifecycle.cs`:**
```csharp
#if MOD_CASINO
using SpacetimeDB;
using System;
using System.Linq;

namespace SandboxRPG.Server;

public static partial class Module
{
    internal static void SeedCasino(ReducerContext ctx)
    {
        // Only seed once — check if casino_building already exists
        if (ctx.Db.PlacedStructure.Iter().Any(s => s.StructureType == "casino_building"))
            return;

        // Casino building POI at (50, 0, 50)
        var building = ctx.Db.PlacedStructure.Insert(new PlacedStructure
        {
            OwnerId = ctx.Sender,
            StructureType = "casino_building",
            PosX = 50f, PosY = 0f, PosZ = 50f,
            RotY = 0f, Health = 1000, MaxHealth = 1000
        });

        // Seed one of each machine inside the building (relative positions)
        var machineTypes = new[]
        {
            ("casino_slot_machine",    52f, 0f, 50f),
            ("casino_blackjack_table", 55f, 0f, 50f),
            ("casino_coin_pusher",     58f, 0f, 50f),
            ("casino_arcade_reaction", 52f, 0f, 53f),
            ("casino_arcade_pattern",  55f, 0f, 53f),
            ("casino_exchange",        58f, 0f, 53f),
        };

        foreach (var (type, x, y, z) in machineTypes)
        {
            var placed = ctx.Db.PlacedStructure.Insert(new PlacedStructure
            {
                OwnerId = ctx.Sender, StructureType = type,
                PosX = x, PosY = y, PosZ = z,
                RotY = 0f, Health = 500, MaxHealth = 500
            });

            // Initialize session rows for each machine
            InitMachineSession(ctx, placed.Id, type);
        }

        SeedCasinoRecipes(ctx);
    }

    private static void InitMachineSession(ReducerContext ctx, ulong machineId, string type)
    {
        switch (type)
        {
            case "casino_slot_machine":
                ctx.Db.SlotSession.Insert(new SlotSession
                    { MachineId = machineId, IsIdle = true });
                break;
            case "casino_blackjack_table":
                ctx.Db.BlackjackGame.Insert(new BlackjackGame
                    { MachineId = machineId, State = 0, RoundId = 1,
                      Deck = BuildDeck(), DealerHand = "", DealerHandHidden = "" });
                break;
            case "casino_coin_pusher":
                ctx.Db.CoinPusherState.Insert(new CoinPusherState
                    { MachineId = machineId, JackpotThreshold = 200 });
                break;
            case "casino_arcade_reaction":
                ctx.Db.ArcadeSession.Insert(new ArcadeSession
                    { MachineId = machineId, GameType = 0, State = 0 });
                break;
            case "casino_arcade_pattern":
                ctx.Db.ArcadeSession.Insert(new ArcadeSession
                    { MachineId = machineId, GameType = 1, State = 0 });
                break;
        }
    }

    internal static string BuildDeck()
    {
        var ranks = new[] { "A","2","3","4","5","6","7","8","9","10","J","Q","K" };
        var suits = new[] { "S","H","D","C" };
        var cards = new System.Collections.Generic.List<string>();
        foreach (var s in suits) foreach (var r in ranks) cards.Add(r + s);
        // Fisher-Yates shuffle with fixed temp RNG
        var rng = new System.Random();
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return string.Join(",", cards);
    }

    private static void SeedCasinoRecipes(ReducerContext ctx)
    {
        var recipes = new[]
        {
            ("casino_slot_machine",     1u, "iron:10,stone:5",  5f),
            ("casino_blackjack_table",  1u, "wood:8,iron:4",    5f),
            ("casino_coin_pusher",      1u, "iron:6,stone:10",  5f),
            ("casino_arcade_reaction",  1u, "iron:8,stone:2",   5f),
            ("casino_arcade_pattern",   1u, "iron:8,stone:2",   5f),
            ("casino_exchange",         1u, "iron:4,stone:4",   5f),
        };
        foreach (var (result, qty, ingredients, time) in recipes)
        {
            if (!ctx.Db.CraftingRecipe.Iter().Any(r => r.ResultItemType == result))
                ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
                {
                    ResultItemType = result, ResultQuantity = qty,
                    Ingredients = ingredients, CraftTimeSeconds = time
                });
        }
    }
}
#endif
```

- [ ] **Call `SeedCasino` from server `Init()` in `server/Lifecycle.cs`, after the existing seed calls:**
```csharp
#if MOD_CASINO
        SeedCasino(ctx);
#endif
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 3.3 — Regenerate bindings

- [ ] **Publish + regenerate + rebuild:**
```bash
cd server
spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
cd ../client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 3.4 — Extend WorldManager to spawn casino structures with Area3D

**Files:**
- Modify: `client/scripts/world/WorldManager.cs`

The existing `CreateStructureVisual` builds mesh nodes. We need to add an `Area3D` on casino machine nodes so `InteractionSystem` can raycast them.

- [ ] **In `WorldManager.cs`, find `CreateStructureVisual` (or `OnStructuresChanged` where nodes are created). After a structure node is created, add this helper call:**

Add this private method to `WorldManager`:
```csharp
private static void AttachStructureInteraction(Node3D node, ulong structureId, string structureType)
{
    if (!structureType.StartsWith("casino_")) return;

    node.SetMeta("structure_id", (long)structureId);
    node.SetMeta("structure_type", structureType);

    var area = new Area3D { CollisionLayer = 2, CollisionMask = 0 };
    var shape = new CollisionShape3D();
    var box = new BoxShape3D { Size = new Vector3(1.5f, 2f, 1.5f) };
    shape.Shape = box;
    area.AddChild(shape);
    node.AddChild(area);
}
```

- [ ] **Call `AttachStructureInteraction(node, structure.Id, structure.StructureType)` immediately after creating each structure node in `OnStructuresChanged` / `CreateStructureVisual`.**

Find the line where the structure `Node3D` is added to the scene and add the call right after it. Example (adapt to match the actual code):
```csharp
var structNode = CreateStructureVisual(structure);
_structures[structure.Id] = structNode;
GetTree().Root.AddChild(structNode); // or wherever nodes are added
AttachStructureInteraction(structNode, structure.Id, structure.StructureType);
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 3.5 — Create CasinoUI router and mod.json

**Files:**
- Create: `client/mods/casino/mod.json`
- Create: `client/mods/casino/CasinoUI.cs`

- [ ] **Create `client/mods/casino/mod.json`:**
```json
{ "id": "casino", "version": "1.0.0", "dependencies": ["currency"] }
```

- [ ] **Create `client/mods/casino/CasinoUI.cs`:**
```csharp
#if MOD_CASINO
using Godot;

/// <summary>
/// Registers all casino structure interaction handlers.
/// Call CasinoUI.Register() from ModManager when casino mod is enabled.
/// </summary>
public static class CasinoUI
{
    public static void Register()
    {
        InteractionSystem.RegisterStructureHandler("casino_slot_machine",    id => SlotMachineUI.Open(id));
        InteractionSystem.RegisterStructureHandler("casino_blackjack_table", id => BlackjackUI.Open(id));
        InteractionSystem.RegisterStructureHandler("casino_coin_pusher",     id => CoinPusherUI.Open(id));
        InteractionSystem.RegisterStructureHandler("casino_arcade_reaction", id => ArcadeUI.TryTriggerReaction(id));
        InteractionSystem.RegisterStructureHandler("casino_arcade_pattern",  id => ArcadeUI.Open(id, isPattern: true));
        InteractionSystem.RegisterStructureHandler("casino_exchange",        id => ExchangeUI.Open(id));
    }
}
#endif
```

- [ ] **In `ModManager.OnSubscriptionApplied`, add after currency section:**
```csharp
#if MOD_CASINO
        if (IsEnabled("casino"))
            CasinoUI.Register();
#endif
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```

> Expected build error: missing types `SlotMachineUI`, `BlackjackUI`, etc. These are stubs — create empty placeholder classes for each now to unblock the build:

- [ ] **Create placeholder stubs (one file each, to be replaced in Chunks 4–7):**

`client/mods/casino/SlotMachineUI.cs`:
```csharp
#if MOD_CASINO
using Godot;
public partial class SlotMachineUI : Node { public static void Open(ulong id) { GD.Print($"[Slot] Open {id}"); } }
#endif
```

`client/mods/casino/BlackjackUI.cs`:
```csharp
#if MOD_CASINO
using Godot;
public partial class BlackjackUI : Node { public static void Open(ulong id) { GD.Print($"[Blackjack] Open {id}"); } }
#endif
```

`client/mods/casino/CoinPusherUI.cs`:
```csharp
#if MOD_CASINO
using Godot;
public partial class CoinPusherUI : Node { public static void Open(ulong id) { GD.Print($"[CoinPusher] Open {id}"); } }
#endif
```

`client/mods/casino/ArcadeUI.cs`:
```csharp
#if MOD_CASINO
using Godot;
public partial class ArcadeUI : Node { public static void Open(ulong id, bool isPattern) { GD.Print($"[Arcade] Open {id} pattern={isPattern}"); } }
#endif
```

`client/mods/currency/ExchangeUI.cs`:
```csharp
#if MOD_CURRENCY
using Godot;
public partial class ExchangeUI : Node { public static void Open(ulong id) { GD.Print($"[Exchange] Open {id}"); } }
#endif
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

> **Smoke test:** Start server, publish, open Godot. Walk to (50,0,50) — casino building should be visible. Approach a machine, press E — Godot Output should print the placeholder log line.

---

### Task 3.6 — Commit chunk 3

- [ ] **Commit:**
```bash
cd C:/Users/Jonas/Documents/GodotGame
git add server/mods/casino/ server/Lifecycle.cs \
        client/mods/casino/ client/mods/currency/ExchangeUI.cs \
        client/scripts/world/WorldManager.cs \
        client/scripts/networking/ModManager.cs \
        client/scripts/networking/SpacetimeDB/
git commit -m "feat: casino world setup — all tables, seeded POI, Area3D interaction, placeholder UIs"
```

---

## Chunk 4: Slot Machine

### Task 4.1 — Create slot machine reducers

**Files:**
- Create: `server/mods/casino/Reducers.cs`

- [ ] **Create `server/mods/casino/Reducers.cs` with SpinSlot and ReleaseSlot:**

```csharp
#if MOD_CASINO
using SpacetimeDB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG.Server;

public static partial class Module
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ulong NowMicros(ReducerContext ctx)
        => (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000;

    private static bool IsDefaultIdentity(Identity id)
        => id == default;

    // ── Slot Machine ──────────────────────────────────────────────────────────

    [Reducer]
    public static void SpinSlot(ReducerContext ctx, ulong machineId, ulong betAmount)
    {
        var session = ctx.Db.SlotSession.MachineId.Find(machineId)
            ?? throw new Exception("Slot machine not found");

        // Piggybacked stale session cleanup
        if (!IsDefaultIdentity(session.Value.PlayerId) &&
            session.Value.ExpiresAt > 0 && NowMicros(ctx) > session.Value.ExpiresAt)
        {
            ctx.Db.SlotSession.Delete(session.Value);
            ctx.Db.SlotSession.Insert(new SlotSession { MachineId = machineId, IsIdle = true });
            session = ctx.Db.SlotSession.MachineId.Find(machineId);
        }

        if (!session.Value.IsIdle && !IsDefaultIdentity(session.Value.PlayerId) &&
            session.Value.PlayerId != ctx.Sender)
            throw new Exception("Machine is occupied");

        if (betAmount == 0) throw new Exception("Bet must be > 0");
        DebitCoins(ctx, ctx.Sender, betAmount, $"slot_bet:{machineId}");

        // Roll reels
        var symbols = new[] { "🍒", "🍋", "🍊", "⭐", "💛", "🔔" };
        var rng = new System.Random();
        string r1 = symbols[rng.Next(symbols.Length)];
        string r2 = symbols[rng.Next(symbols.Length)];
        string r3 = symbols[rng.Next(symbols.Length)];
        string reels = $"{r1}|{r2}|{r3}";

        // Payout
        ulong multiplier = 0;
        if (r1 == r2 && r2 == r3)
        {
            multiplier = r1 switch { "💛" => 100, "⭐" => 20, "🍒" => 10, _ => 5 };
        }
        else if (r1 == r2 || r2 == r3 || r1 == r3) multiplier = 2;

        ulong winAmount = betAmount * multiplier;
        if (winAmount > 0)
            CreditCoins(ctx, ctx.Sender, winAmount, $"slot_win:{machineId}");

        ctx.Db.SlotSession.Delete(session.Value);
        ctx.Db.SlotSession.Insert(new SlotSession
        {
            MachineId = machineId,
            PlayerId = ctx.Sender,
            Reels = reels,
            IsIdle = false,
            Bet = betAmount,
            WinAmount = winAmount,
            ExpiresAt = NowMicros(ctx) + 30_000_000 // 30s in micros
        });
    }

    [Reducer]
    public static void ReleaseSlot(ReducerContext ctx, ulong machineId)
    {
        var session = ctx.Db.SlotSession.MachineId.Find(machineId)
            ?? throw new Exception("Slot machine not found");
        if (session.Value.PlayerId != ctx.Sender)
            throw new Exception("Not your session");
        ctx.Db.SlotSession.Delete(session.Value);
        ctx.Db.SlotSession.Insert(new SlotSession { MachineId = machineId, IsIdle = true });
    }
}
#endif
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 4.2 — Regenerate bindings

- [ ] **Publish + regenerate + rebuild:**
```bash
cd server
spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
cd ../client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 4.3 — Create SlotMachineUI with SubViewport reel display

**Files:**
- Modify: `client/mods/casino/SlotMachineUI.cs`

- [ ] **Replace the stub in `client/mods/casino/SlotMachineUI.cs`:**

```csharp
#if MOD_CASINO
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Manages the slot machine SubViewport display and bet popup.
/// Attaches to the machine node; one instance per placed slot machine.
/// </summary>
public partial class SlotMachineUI : Node
{
    private static readonly System.Collections.Generic.Dictionary<ulong, SlotMachineUI> _instances = new();

    private ulong _machineId;
    private SubViewport _viewport;
    private Label _reelLabel;
    private Label _winLabel;
    private CanvasLayer _betPopup;
    private SpinBox _betSpinBox;
    private bool _animating;

    public static void Open(ulong machineId)
    {
        // Show bet popup as a CanvasLayer overlay
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowBetPopup();
    }

    public static SlotMachineUI AttachToNode(Node3D machineNode, ulong machineId)
    {
        var ui = new SlotMachineUI();
        ui._machineId = machineId;
        machineNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        // Build SubViewport for the machine screen
        _viewport = new SubViewport { Size = new Vector2I(256, 128), TransparentBg = true };
        AddChild(_viewport);

        var bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.2f), Size = new Vector2(256, 128) };
        _viewport.AddChild(bg);

        _reelLabel = new Label { Text = "🎰 🎰 🎰", Position = new Vector2(20, 30) };
        _reelLabel.AddThemeFontSizeOverride("font_size", 32);
        _viewport.AddChild(_reelLabel);

        _winLabel = new Label { Position = new Vector2(20, 90) };
        _winLabel.AddThemeFontSizeOverride("font_size", 14);
        _viewport.AddChild(_winLabel);

        // Attach viewport texture to the Screen mesh on the machine node
        ApplyScreenTexture();

        // Subscribe to SlotSession updates
        GameManager.Instance.Conn.Db.SlotSession.OnUpdate += OnSessionUpdate;
    }

    private void ApplyScreenTexture()
    {
        var parent = GetParent<Node3D>();
        if (parent == null) return;
        var screenMesh = parent.FindChild("Screen") as MeshInstance3D;
        if (screenMesh == null) return;
        var mat = new StandardMaterial3D { AlbedoTexture = _viewport.GetTexture() };
        screenMesh.MaterialOverride = mat;
    }

    private void OnSessionUpdate(SlotSession oldVal, SlotSession newVal, SpacetimeDB.ReducerEvent<SpacetimeDB.Types.Reducer> evt)
    {
        if (newVal.MachineId != _machineId) return;
        var captured = newVal;
        Callable.From(() => UpdateDisplay(captured)).CallDeferred();
    }

    private void UpdateDisplay(SlotSession session)
    {
        if (session.IsIdle)
        {
            _reelLabel.Text = "🎰 🎰 🎰";
            _winLabel.Text = "Insert coins!";
            return;
        }

        // Play spin animation then show result
        if (!_animating)
        {
            _animating = true;
            AnimateReels(session);
        }
    }

    private async void AnimateReels(SlotSession session)
    {
        // Fake spin: cycle through symbols for 1.5s
        var symbols = new[] { "🍒", "🍋", "🍊", "⭐", "💛", "🔔" };
        var rng = new System.Random();
        double elapsed = 0;
        while (elapsed < 1.5)
        {
            _reelLabel.Text = $"{symbols[rng.Next(6)]} {symbols[rng.Next(6)]} {symbols[rng.Next(6)]}";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        // Show real result
        var parts = session.Reels.Split('|');
        _reelLabel.Text = string.Join(" ", parts);
        _winLabel.Text = session.WinAmount > 0 ? $"WIN: {session.WinAmount} 🟤" : "No luck!";
        _animating = false;

        // Auto-release after 3s if this is our session
        var localPlayer = GameManager.Instance.GetLocalPlayer();
        if (localPlayer != null && session.PlayerId == localPlayer.Identity)
        {
            await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
            GameManager.Instance.Conn.Reducers.ReleaseSlot(_machineId);
        }
    }

    private void ShowBetPopup()
    {
        if (_betPopup != null) { _betPopup.Visible = true; return; }
        _betPopup = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(220, 120);
        panel.Position = new Vector2(-110, -60);

        var vbox = new VBoxContainer { Position = new Vector2(10, 10), Size = new Vector2(200, 100) };
        var lbl = new Label { Text = "Spin amount (Copper):" };
        _betSpinBox = new SpinBox { MinValue = 1, MaxValue = 10000, Value = 10, Step = 1 };
        var btn = new Button { Text = "SPIN" };
        btn.Pressed += OnSpinPressed;

        vbox.AddChild(lbl);
        vbox.AddChild(_betSpinBox);
        vbox.AddChild(btn);
        panel.AddChild(vbox);
        _betPopup.AddChild(panel);
        GetTree().Root.AddChild(_betPopup);
    }

    private void OnSpinPressed()
    {
        _betPopup.Visible = false;
        ulong bet = (ulong)_betSpinBox.Value;
        GameManager.Instance.Conn.Reducers.SpinSlot(_machineId, bet);
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
        GameManager.Instance.Conn.Db.SlotSession.OnUpdate -= OnSessionUpdate;
    }
}
#endif
```

- [ ] **In `WorldManager.AttachStructureInteraction` (Task 3.4) or a new post-spawn hook, attach `SlotMachineUI` when spawning a `casino_slot_machine`:**

In `WorldManager`, after `AttachStructureInteraction`:
```csharp
#if MOD_CASINO
        if (structureType == "casino_slot_machine")
            SlotMachineUI.AttachToNode(node, structureId);
        // (other types added in Chunks 5–7)
#endif
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

> **Smoke test:** Approach slot machine, press E → bet popup appears. Enter bet, press SPIN → reels animate for ~1.5s, result shown on machine screen. Balance deducted, win credited if matched.

---

### Task 4.4 — Commit chunk 4

- [ ] **Commit:**
```bash
cd C:/Users/Jonas/Documents/GodotGame
git add server/mods/casino/Reducers.cs client/mods/casino/SlotMachineUI.cs \
        client/scripts/world/WorldManager.cs \
        client/scripts/networking/SpacetimeDB/
git commit -m "feat: slot machine — SpinSlot/ReleaseSlot reducers, SubViewport reel animation, bet popup"
```

---

## Chunk 5: Blackjack

### Task 5.1 — Create blackjack reducers

**Files:**
- Modify: `server/mods/casino/Reducers.cs`

- [ ] **Append these blackjack reducers to `server/mods/casino/Reducers.cs` inside `public static partial class Module`:**

```csharp
    // ── Blackjack helpers ─────────────────────────────────────────────────────

    private static int CardValue(string card)
    {
        string rank = card[..^1]; // strip suit
        return rank switch { "A" => 11, "J" or "Q" or "K" => 10, _ => int.Parse(rank) };
    }

    private static int HandValue(string hand)
    {
        if (string.IsNullOrEmpty(hand)) return 0;
        var cards = hand.Split(',');
        int total = 0, aces = 0;
        foreach (var c in cards)
        {
            int v = CardValue(c);
            if (v == 11) aces++;
            total += v;
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }

    private static (string card, string remainingDeck) DrawCard(string deck)
    {
        var cards = deck.Split(',');
        return (cards[0], string.Join(",", cards[1..]));
    }

    private static void CheckAllSeatsResolved(ReducerContext ctx, ulong machineId, uint roundId)
    {
        var seats = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == roundId).ToList();
        bool allDone = seats.All(s => s.State is 2 or 3 or 4); // Standing/Bust/Done
        if (!allDone) return;

        RunDealerTurn(ctx, machineId, roundId);
    }

    private static void RunDealerTurn(ReducerContext ctx, ulong machineId, uint roundId)
    {
        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId)
            ?? throw new Exception("Game not found");
        var g = game.Value;
        g.State = 2; // DealerTurn
        string deck = g.Deck;

        while (HandValue(g.DealerHand) < 17)
        {
            var (card, remaining) = DrawCard(deck);
            g.DealerHand = string.IsNullOrEmpty(g.DealerHand) ? card : g.DealerHand + "," + card;
            deck = remaining;
        }
        g.Deck = deck;
        g.DealerHandHidden = g.DealerHand; // reveal

        int dealerTotal = HandValue(g.DealerHand);
        var seats = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == roundId).ToList();

        foreach (var seat in seats)
        {
            int playerTotal = HandValue(seat.Hand);
            bool playerBust = playerTotal > 21;
            bool dealerBust = dealerTotal > 21;
            ulong payout = 0;

            if (!playerBust)
            {
                if (dealerBust || playerTotal > dealerTotal) payout = seat.Bet * 2; // Win
                else if (playerTotal == dealerTotal) payout = seat.Bet;             // Push
            }
            if (payout > 0)
                CreditCoins(ctx, seat.PlayerId, payout, $"blackjack_win:{machineId}");
        }

        g.State = 3; // Payout
        ctx.Db.BlackjackGame.Delete(game.Value);
        ctx.Db.BlackjackGame.Insert(g);
    }

    // ── Blackjack reducers ────────────────────────────────────────────────────

    [Reducer]
    public static void JoinBlackjack(ReducerContext ctx, ulong machineId, byte seatIndex)
    {
        if (seatIndex > 3) throw new Exception("Seat index must be 0–3");

        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId)
            ?? throw new Exception("Blackjack table not found");

        // Reset from Payout state
        if (game.Value.State == 3)
        {
            // Clear old seats for previous round
            foreach (var old in ctx.Db.BlackjackSeat.Iter()
                .Where(s => s.MachineId == machineId && s.RoundId == game.Value.RoundId).ToList())
                ctx.Db.BlackjackSeat.Delete(old);
            var reset = game.Value;
            reset.State = 0;
            ctx.Db.BlackjackGame.Delete(game.Value);
            ctx.Db.BlackjackGame.Insert(reset);
            game = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        }

        if (game.Value.State != 0) throw new Exception("Round already in progress");

        // Seat uniqueness check
        bool taken = ctx.Db.BlackjackSeat.Iter().Any(s =>
            s.MachineId == machineId && s.SeatIndex == seatIndex && s.RoundId == game.Value.RoundId);
        if (taken) throw new Exception("Seat already taken");

        // Already sitting?
        bool alreadySitting = ctx.Db.BlackjackSeat.Iter().Any(s =>
            s.MachineId == machineId && s.PlayerId == ctx.Sender && s.RoundId == game.Value.RoundId);
        if (alreadySitting) throw new Exception("Already seated");

        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            MachineId = machineId, SeatIndex = seatIndex,
            PlayerId = ctx.Sender, Hand = "", Bet = 0, State = 0,
            RoundId = game.Value.RoundId
        });
    }

    [Reducer]
    public static void PlaceBet(ReducerContext ctx, ulong machineId, ulong amount)
    {
        if (amount == 0) throw new Exception("Bet must be > 0");
        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId)
            ?? throw new Exception("Table not found");
        if (game.Value.State != 0) throw new Exception("Betting closed");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender
                              && s.RoundId == game.Value.RoundId);
        if (seat.PlayerId != ctx.Sender) throw new Exception("Not seated");
        if (seat.Bet > 0) throw new Exception("Bet already placed");

        DebitCoins(ctx, ctx.Sender, amount, $"blackjack_bet:{machineId}");
        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = seat.Hand, Bet = amount,
            State = seat.State, RoundId = seat.RoundId
        });
    }

    [Reducer]
    public static void StartBlackjackRound(ReducerContext ctx, ulong machineId)
    {
        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId)
            ?? throw new Exception("Table not found");
        if (game.Value.State != 0 && game.Value.State != 3)
            throw new Exception("Round already started");

        // If coming from Payout, reset first
        if (game.Value.State == 3)
        {
            foreach (var old in ctx.Db.BlackjackSeat.Iter()
                .Where(s => s.MachineId == machineId).ToList())
                ctx.Db.BlackjackSeat.Delete(old);
        }

        var seatsWithBets = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.Bet > 0).ToList();
        if (seatsWithBets.Count == 0) throw new Exception("No players with bets");

        // New round
        uint newRoundId = game.Value.RoundId + 1;
        string deck = BuildDeck();

        // Deal 2 cards to each player and dealer
        string dealerHand = "";
        foreach (var seat in seatsWithBets)
        {
            string hand = "";
            for (int i = 0; i < 2; i++)
            {
                var (card, remaining) = DrawCard(deck);
                deck = remaining;
                hand = string.IsNullOrEmpty(hand) ? card : hand + "," + card;
            }
            ctx.Db.BlackjackSeat.Delete(seat);
            ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
            {
                Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
                PlayerId = seat.PlayerId, Hand = hand, Bet = seat.Bet,
                State = 1, // Acting
                RoundId = newRoundId
            });
        }
        for (int i = 0; i < 2; i++) { var (card, rem) = DrawCard(deck); deck = rem; dealerHand = string.IsNullOrEmpty(dealerHand) ? card : dealerHand + "," + card; }

        // Advance first seat to Acting, rest to Waiting
        bool firstSet = false;
        var allSeats = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == newRoundId)
            .OrderBy(s => s.SeatIndex).ToList();
        foreach (var s in allSeats)
        {
            ctx.Db.BlackjackSeat.Delete(s);
            ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
            {
                Id = s.Id, MachineId = s.MachineId, SeatIndex = s.SeatIndex,
                PlayerId = s.PlayerId, Hand = s.Hand, Bet = s.Bet,
                State = (byte)(!firstSet ? (firstSet = true, 1).Item2 : 0),
                RoundId = newRoundId
            });
        }

        var hiddenDealer = dealerHand.Split(',')[0] + ",??";
        ctx.Db.BlackjackGame.Delete(game.Value);
        ctx.Db.BlackjackGame.Insert(new BlackjackGame
        {
            MachineId = machineId, State = 1, // PlayerTurns
            DealerHand = dealerHand, DealerHandHidden = hiddenDealer,
            Deck = deck, RoundId = newRoundId
        });
    }

    [Reducer]
    public static void HitBlackjack(ReducerContext ctx, ulong machineId)
    {
        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId)
            ?? throw new Exception("Table not found");
        if (game.Value.State != 1) throw new Exception("Not in player turns");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender
                              && s.RoundId == game.Value.RoundId && s.State == 1);
        if (seat.PlayerId != ctx.Sender) throw new Exception("Not your turn");

        var (card, deck) = DrawCard(game.Value.Deck);
        string newHand = seat.Hand + "," + card;
        byte newState = HandValue(newHand) > 21 ? (byte)3 : (byte)1; // Bust or still Acting

        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = newHand, Bet = seat.Bet,
            State = newState, RoundId = seat.RoundId
        });
        var g = game.Value; g.Deck = deck;
        ctx.Db.BlackjackGame.Delete(game.Value);
        ctx.Db.BlackjackGame.Insert(g);

        if (newState == 3) AdvanceToNextSeat(ctx, machineId, game.Value.RoundId);
    }

    [Reducer]
    public static void StandBlackjack(ReducerContext ctx, ulong machineId)
    {
        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId)
            ?? throw new Exception("Table not found");
        if (game.Value.State != 1) throw new Exception("Not in player turns");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender
                              && s.RoundId == game.Value.RoundId && s.State == 1);
        if (seat.PlayerId != ctx.Sender) throw new Exception("Not your turn");

        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = seat.Hand, Bet = seat.Bet,
            State = 2, RoundId = seat.RoundId // Standing
        });
        AdvanceToNextSeat(ctx, machineId, game.Value.RoundId);
    }

    private static void AdvanceToNextSeat(ReducerContext ctx, ulong machineId, uint roundId)
    {
        // Find next Waiting seat (lowest SeatIndex)
        var next = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == roundId && s.State == 0)
            .OrderBy(s => s.SeatIndex).FirstOrDefault();

        if (next.RoundId == roundId) // found a waiting seat
        {
            ctx.Db.BlackjackSeat.Delete(next);
            ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
            {
                Id = next.Id, MachineId = next.MachineId, SeatIndex = next.SeatIndex,
                PlayerId = next.PlayerId, Hand = next.Hand, Bet = next.Bet,
                State = 1, RoundId = next.RoundId // Acting
            });
        }
        else
        {
            // All seats done — run dealer turn
            CheckAllSeatsResolved(ctx, machineId, roundId);
        }
    }

    [Reducer]
    public static void LeaveBlackjack(ReducerContext ctx, ulong machineId)
    {
        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender);
        if (seat.PlayerId != ctx.Sender) return;

        // Refund bet if still in WaitingForPlayers
        if (game != null && game.Value.State == 0 && seat.Bet > 0)
            CreditCoins(ctx, ctx.Sender, seat.Bet, "blackjack_leave_refund");

        ctx.Db.BlackjackSeat.Delete(seat);
    }

    [Reducer]
    public static void SkipSeat(ReducerContext ctx, ulong machineId, byte seatIndex)
    {
        var game = ctx.Db.BlackjackGame.MachineId.Find(machineId)
            ?? throw new Exception("Table not found");
        if (game.Value.State != 1) throw new Exception("Not in player turns");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.SeatIndex == seatIndex
                              && s.RoundId == game.Value.RoundId && s.State == 1);
        if (seat.RoundId != game.Value.RoundId) throw new Exception("Seat not found or not active");

        // Verify target is disconnected
        var target = ctx.Db.Player.Identity.Find(seat.PlayerId);
        if (target != null && target.Value.IsOnline)
            throw new Exception("Player is still connected");

        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = seat.Hand, Bet = seat.Bet,
            State = 2, RoundId = seat.RoundId // Force stand
        });
        AdvanceToNextSeat(ctx, machineId, game.Value.RoundId);
    }
```

- [ ] **Build server:**
```bash
cd server && spacetime build
```
Expected: 0 errors.

---

### Task 5.2 — Regenerate bindings

- [ ] **Publish + regenerate + rebuild:**
```bash
cd server
spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
cd ../client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

---

### Task 5.3 — Create BlackjackUI with 3D card spawner

**Files:**
- Modify: `client/mods/casino/BlackjackUI.cs`

- [ ] **Replace the blackjack stub:**

```csharp
#if MOD_CASINO
using Godot;
using System.Collections.Generic;
using SpacetimeDB.Types;

/// <summary>
/// Manages the blackjack table interaction:
/// - Shows seat selection popup
/// - Spawns 3D card MeshInstance3D objects on the felt surface
/// - Subscribes to BlackjackGame + BlackjackSeat updates
/// </summary>
public partial class BlackjackUI : Node3D
{
    private static readonly Dictionary<ulong, BlackjackUI> _instances = new();

    private ulong _machineId;
    private CanvasLayer _popup;
    private readonly Dictionary<string, MeshInstance3D> _cardNodes = new();
    private static readonly Vector3 FeltOrigin = new(0f, 0.55f, 0f); // above table surface
    private const float CardSpacing = 0.22f;

    public static void Open(ulong machineId)
    {
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowSeatPopup();
    }

    public static BlackjackUI AttachToNode(Node3D tableNode, ulong machineId)
    {
        var ui = new BlackjackUI { _machineId = machineId };
        tableNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        GameManager.Instance.Conn.Db.BlackjackSeat.OnInsert += (_, _) => CallDeferred(nameof(RefreshCards));
        GameManager.Instance.Conn.Db.BlackjackSeat.OnUpdate += (_, _, _) => CallDeferred(nameof(RefreshCards));
        GameManager.Instance.Conn.Db.BlackjackSeat.OnDelete += (_, _) => CallDeferred(nameof(RefreshCards));
        GameManager.Instance.Conn.Db.BlackjackGame.OnUpdate += (_, _, _) => CallDeferred(nameof(RefreshCards));
    }

    private void ShowSeatPopup()
    {
        if (_popup != null) { _popup.Visible = true; return; }
        _popup = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(260, 180);
        panel.Position = new Vector2(-130, -90);

        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = "Choose a seat (0–3):" });

        for (byte i = 0; i < 4; i++)
        {
            byte idx = i;
            var btn = new Button { Text = $"Seat {idx}" };
            btn.Pressed += () => OnSeatChosen(idx);
            vbox.AddChild(btn);
        }
        var leave = new Button { Text = "Leave Table" };
        leave.Pressed += () => {
            GameManager.Instance.Conn.Reducers.LeaveBlackjack(_machineId);
            _popup.Visible = false;
        };
        vbox.AddChild(leave);
        panel.AddChild(vbox);
        _popup.AddChild(panel);
        GetTree().Root.AddChild(_popup);
    }

    private void OnSeatChosen(byte seatIndex)
    {
        _popup.Visible = false;
        GameManager.Instance.Conn.Reducers.JoinBlackjack(_machineId, seatIndex);
        // Show bet + start UI
        ShowBetPopup();
    }

    private void ShowBetPopup()
    {
        var layer = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(240, 140);
        panel.Position = new Vector2(-120, -70);
        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = "Your bet (Copper):" });
        var spin = new SpinBox { MinValue = 1, MaxValue = 10000, Value = 10 };
        vbox.AddChild(spin);
        var betBtn = new Button { Text = "Place Bet" };
        betBtn.Pressed += () => {
            GameManager.Instance.Conn.Reducers.PlaceBet(_machineId, (ulong)spin.Value);
            layer.QueueFree();
        };
        var startBtn = new Button { Text = "Start Round" };
        startBtn.Pressed += () => {
            GameManager.Instance.Conn.Reducers.StartBlackjackRound(_machineId);
            layer.QueueFree();
        };
        vbox.AddChild(betBtn);
        vbox.AddChild(startBtn);
        panel.AddChild(vbox);
        layer.AddChild(panel);
        GetTree().Root.AddChild(layer);
    }

    private void RefreshCards()
    {
        // Clear existing card nodes
        foreach (var node in _cardNodes.Values) node.QueueFree();
        _cardNodes.Clear();

        var game = GameManager.Instance.Conn.Db.BlackjackGame.MachineId.Find(_machineId);
        if (game == null) return;

        // Spawn dealer cards (top of table)
        string dealerHand = game.Value.State >= 2 ? game.Value.DealerHand : game.Value.DealerHandHidden;
        SpawnCardRow(dealerHand, new Vector3(-0.5f, 0f, -0.3f), isDealer: true);

        // Spawn player hands
        var seats = new List<BlackjackSeat>();
        foreach (var s in GameManager.Instance.Conn.Db.BlackjackSeat.Iter())
            if (s.MachineId == _machineId && s.RoundId == game.Value.RoundId)
                seats.Add(s);

        for (int si = 0; si < seats.Count; si++)
        {
            float xOffset = -0.6f + si * 0.4f;
            SpawnCardRow(seats[si].Hand, new Vector3(xOffset, 0f, 0.4f), isDealer: false);
        }
    }

    private void SpawnCardRow(string hand, Vector3 origin, bool isDealer)
    {
        if (string.IsNullOrEmpty(hand)) return;
        var cards = hand.Split(',');
        for (int i = 0; i < cards.Length; i++)
        {
            var node = CreateCardMesh(cards[i]);
            node.Position = origin + new Vector3(i * CardSpacing, 0, 0);
            AddChild(node);
            _cardNodes[$"{origin}_{i}"] = node;
        }
    }

    private static MeshInstance3D CreateCardMesh(string code)
    {
        var mesh = new MeshInstance3D();
        var box = new BoxMesh { Size = new Vector3(0.15f, 0.002f, 0.22f) };
        mesh.Mesh = box;
        var mat = new StandardMaterial3D();
        bool isHidden = code == "??";
        mat.AlbedoColor = isHidden ? new Color(0.1f, 0.1f, 0.8f) : Colors.White;
        // Label the card with a Label3D
        if (!isHidden)
        {
            var label = new Label3D { Text = code, FontSize = 16 };
            label.Position = new Vector3(0, 0.01f, 0);
            label.RotationDegrees = new Vector3(-90, 0, 0);
            mesh.AddChild(label);
        }
        mesh.MaterialOverride = mat;
        return mesh;
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
    }
}
#endif
```

- [ ] **In WorldManager, after casino structure spawn, add:**
```csharp
#if MOD_CASINO
        if (structureType == "casino_blackjack_table")
            BlackjackUI.AttachToNode(node, structureId);
#endif
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```

> **Smoke test:** Two players join the blackjack table, place bets, start round — cards appear as 3D objects on the table felt. Hit/Stand advance turns. Dealer draws. Winnings credited automatically.

---

### Task 5.4 — Commit chunk 5

- [ ] **Commit:**
```bash
cd C:/Users/Jonas/Documents/GodotGame
git add server/mods/casino/Reducers.cs client/mods/casino/BlackjackUI.cs \
        client/scripts/world/WorldManager.cs \
        client/scripts/networking/SpacetimeDB/
git commit -m "feat: multiplayer blackjack — full round lifecycle, 3D card meshes, seat/bet/start flow"
```

---

## Chunk 6: Coin Pusher

### Task 6.1 — Add PushCoin reducer

**Files:**
- Modify: `server/mods/casino/Reducers.cs`

- [ ] **Append PushCoin reducer:**

```csharp
    // ── Coin Pusher ───────────────────────────────────────────────────────────

    [Reducer]
    public static void PushCoin(ReducerContext ctx, ulong machineId, ulong copperAmount)
    {
        if (copperAmount == 0) throw new Exception("Amount must be > 0");
        var state = ctx.Db.CoinPusherState.MachineId.Find(machineId)
            ?? throw new Exception("Coin pusher not found");

        DebitCoins(ctx, ctx.Sender, copperAmount, $"coin_push:{machineId}");

        uint newCount = state.Value.CoinCount + 1;
        ulong newPool = state.Value.CopperPool + copperAmount;
        ulong now = NowMicros(ctx);

        if (newCount >= state.Value.JackpotThreshold)
        {
            // Jackpot — pay out pool to this player, reset
            CreditCoins(ctx, ctx.Sender, newPool, $"coin_pusher_jackpot:{machineId}");
            ctx.Db.CoinPusherState.Delete(state.Value);
            ctx.Db.CoinPusherState.Insert(new CoinPusherState
            {
                MachineId = machineId, CoinCount = 0, CopperPool = 0,
                LastPusherId = ctx.Sender, LastPushTime = now,
                JackpotThreshold = state.Value.JackpotThreshold
            });
        }
        else
        {
            ctx.Db.CoinPusherState.Delete(state.Value);
            ctx.Db.CoinPusherState.Insert(new CoinPusherState
            {
                MachineId = machineId, CoinCount = newCount, CopperPool = newPool,
                LastPusherId = ctx.Sender, LastPushTime = now,
                JackpotThreshold = state.Value.JackpotThreshold
            });
        }
    }
```

- [ ] **Build and regenerate:**
```bash
cd server && spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
cd ../client && dotnet build SandboxRPG.csproj
```

---

### Task 6.2 — Create CoinPusherUI with RigidBody3D coins

**Files:**
- Modify: `client/mods/casino/CoinPusherUI.cs`

- [ ] **Replace the coin pusher stub:**

```csharp
#if MOD_CASINO
using Godot;
using System.Collections.Generic;
using SpacetimeDB.Types;

/// <summary>
/// Spawns RigidBody3D coin physics objects when CoinCount increases.
/// On join, scatters CoinCount coins using MachineId as RNG seed.
/// Shows a small "Push" button overlay on E-press.
/// </summary>
public partial class CoinPusherUI : Node3D
{
    private static readonly Dictionary<ulong, CoinPusherUI> _instances = new();

    private ulong _machineId;
    private uint _spawnedCount;
    private CanvasLayer _popup;
    private static readonly Vector3 PushEntry = new(0f, 1.2f, 0f);

    public static void Open(ulong machineId)
    {
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowPushPopup();
    }

    public static CoinPusherUI AttachToNode(Node3D pusherNode, ulong machineId)
    {
        var ui = new CoinPusherUI { _machineId = machineId };
        pusherNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        // Rebuild initial coin pile from current state
        var state = GameManager.Instance.Conn.Db.CoinPusherState.MachineId.Find(_machineId);
        if (state != null) ScatterCoins(state.Value.CoinCount);

        GameManager.Instance.Conn.Db.CoinPusherState.OnUpdate += OnStateUpdate;
    }

    private void OnStateUpdate(CoinPusherState old, CoinPusherState newState, SpacetimeDB.ReducerEvent<SpacetimeDB.Types.Reducer> evt)
    {
        if (newState.MachineId != _machineId) return;
        var capturedNew = newState; var capturedOld = old;
        Callable.From(() => HandleStateChange(capturedNew, capturedOld)).CallDeferred();
    }

    private void HandleStateChange(CoinPusherState newState, CoinPusherState old)
    {
        if (newState.CoinCount > old.CoinCount)
        {
            // Spawn one new coin per push
            SpawnCoin(PushEntry + new Vector3(0, 0.2f, 0), applyImpulse: true);
            _spawnedCount++;
        }
        else if (newState.CoinCount == 0 && old.CoinCount > 0)
        {
            // Jackpot reset — clear all coin nodes
            foreach (var child in GetChildren())
                if (child is RigidBody3D) child.QueueFree();
            _spawnedCount = 0;
        }
    }

    private void ScatterCoins(uint count)
    {
        var rng = new System.Random((int)_machineId); // deterministic per machine
        for (uint i = 0; i < count; i++)
        {
            var pos = new Vector3(
                (float)(rng.NextDouble() * 0.8 - 0.4),
                0.5f + (float)(rng.NextDouble() * 0.3),
                (float)(rng.NextDouble() * 0.8 - 0.4)
            );
            SpawnCoin(pos, applyImpulse: false);
        }
        _spawnedCount = count;
    }

    private void SpawnCoin(Vector3 localPos, bool applyImpulse)
    {
        var body = new RigidBody3D { Mass = 0.01f };
        var mesh = new MeshInstance3D();
        var cyl = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.008f };
        mesh.Mesh = cyl;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.65f, 0.1f) };
        mesh.MaterialOverride = mat;

        var col = new CollisionShape3D();
        var cShape = new CylinderShape3D { Radius = 0.04f, Height = 0.008f };
        col.Shape = cShape;

        body.AddChild(mesh);
        body.AddChild(col);
        body.Position = localPos;
        AddChild(body);

        if (applyImpulse)
        {
            var b = body;
            Callable.From(() => b.ApplyCentralImpulse(new Vector3(0, -0.5f, 0.1f))).CallDeferred();
        }
    }

    private void ShowPushPopup()
    {
        if (_popup != null) { _popup.Visible = true; return; }
        _popup = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(200, 120);
        panel.Position = new Vector2(-100, -60);

        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = "Push coins (Copper per push):" });
        var spin = new SpinBox { MinValue = 1, MaxValue = 1000, Value = 5 };
        vbox.AddChild(spin);
        var btn = new Button { Text = "PUSH" };
        btn.Pressed += () => {
            _popup.Visible = false;
            GameManager.Instance.Conn.Reducers.PushCoin(_machineId, (ulong)spin.Value);
        };
        vbox.AddChild(btn);
        panel.AddChild(vbox);
        _popup.AddChild(panel);
        GetTree().Root.AddChild(_popup);
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
        GameManager.Instance.Conn.Db.CoinPusherState.OnUpdate -= OnStateUpdate;
    }
}
#endif
```

- [ ] **In WorldManager, add:**
```csharp
#if MOD_CASINO
        if (structureType == "casino_coin_pusher")
            CoinPusherUI.AttachToNode(node, structureId);
#endif
```

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```

> **Smoke test:** Approach coin pusher, press E, enter amount, click PUSH — a gold coin `RigidBody3D` spawns at the top and falls onto the pile. After 200 pushes, jackpot payout credited.

---

### Task 6.3 — Commit chunk 6

- [ ] **Commit:**
```bash
cd C:/Users/Jonas/Documents/GodotGame
git add server/mods/casino/Reducers.cs client/mods/casino/CoinPusherUI.cs \
        client/scripts/world/WorldManager.cs \
        client/scripts/networking/SpacetimeDB/
git commit -m "feat: coin pusher — PushCoin reducer, RigidBody3D coin physics, jackpot payout"
```

---

## Chunk 7: Arcade Machines

### Task 7.1 — Add arcade reducers

**Files:**
- Modify: `server/mods/casino/Reducers.cs`

- [ ] **Append arcade reducers:**

```csharp
    // ── Arcade machines ───────────────────────────────────────────────────────

    [Reducer]
    public static void StartArcade(ReducerContext ctx, ulong machineId, ulong bet)
    {
        if (bet == 0) throw new Exception("Bet must be > 0");
        var session = ctx.Db.ArcadeSession.MachineId.Find(machineId)
            ?? throw new Exception("Arcade machine not found");

        // Piggybacked stale session cleanup
        if (!IsDefaultIdentity(session.Value.PlayerId) &&
            session.Value.ExpiresAt > 0 && NowMicros(ctx) > session.Value.ExpiresAt)
        {
            var cleared = session.Value;
            cleared.PlayerId = default;
            cleared.State = 0;
            ctx.Db.ArcadeSession.Delete(session.Value);
            ctx.Db.ArcadeSession.Insert(cleared);
            session = ctx.Db.ArcadeSession.MachineId.Find(machineId);
        }

        if (!IsDefaultIdentity(session.Value.PlayerId) && session.Value.PlayerId != ctx.Sender)
            throw new Exception("Machine occupied");

        DebitCoins(ctx, ctx.Sender, bet, $"arcade_bet:{machineId}");

        string challenge;
        ulong expiry;
        if (session.Value.GameType == 0) // Reaction
        {
            var rng = new System.Random();
            int targetMs = rng.Next(1000, 3000);
            int windowMs = 150;
            challenge = $"{targetMs}:{windowMs}";
            expiry = NowMicros(ctx) + 10_000_000; // 10s
        }
        else // Pattern
        {
            var chars = new[] { 'R', 'G', 'B', 'Y' };
            var rng = new System.Random();
            challenge = new string(System.Linq.Enumerable.Range(0, 5)
                .Select(_ => chars[rng.Next(chars.Length)]).ToArray());
            expiry = NowMicros(ctx) + 15_000_000; // 15s
        }

        ctx.Db.ArcadeSession.Delete(session.Value);
        ctx.Db.ArcadeSession.Insert(new ArcadeSession
        {
            MachineId = machineId, PlayerId = ctx.Sender,
            GameType = session.Value.GameType, State = 1, // Active
            Bet = bet, ChallengeData = challenge,
            StartTime = NowMicros(ctx), ExpiresAt = expiry
        });
    }

    [Reducer]
    public static void ArcadeInputReaction(ReducerContext ctx, ulong machineId)
    {
        var session = ctx.Db.ArcadeSession.MachineId.Find(machineId)
            ?? throw new Exception("Session not found");
        if (session.Value.State != 1 || session.Value.PlayerId != ctx.Sender)
            throw new Exception("Not your active session");

        var parts = session.Value.ChallengeData.Split(':');
        int targetMs = int.Parse(parts[0]);
        int windowMs = int.Parse(parts[1]);

        // Use server timestamp exclusively — no client-supplied timing
        long elapsedMs = (long)((NowMicros(ctx) - session.Value.StartTime) / 1000);
        bool hit = Math.Abs(elapsedMs - targetMs) <= (windowMs + 200);

        if (hit)
            CreditCoins(ctx, ctx.Sender, session.Value.Bet * 3, $"arcade_reaction_win:{machineId}");

        ctx.Db.ArcadeSession.Delete(session.Value);
        ctx.Db.ArcadeSession.Insert(new ArcadeSession
        {
            MachineId = machineId, PlayerId = default, GameType = session.Value.GameType,
            State = 0, Bet = 0, ChallengeData = hit ? "HIT" : "MISS", ExpiresAt = 0
        });
    }

    [Reducer]
    public static void ArcadeInputPattern(ReducerContext ctx, ulong machineId, string playerSequence)
    {
        var session = ctx.Db.ArcadeSession.MachineId.Find(machineId)
            ?? throw new Exception("Session not found");
        if (session.Value.State != 1 || session.Value.PlayerId != ctx.Sender)
            throw new Exception("Not your active session");

        bool correct = playerSequence == session.Value.ChallengeData;
        bool expired = NowMicros(ctx) > session.Value.ExpiresAt;

        if (correct && !expired)
            CreditCoins(ctx, ctx.Sender, session.Value.Bet * 2, $"arcade_pattern_win:{machineId}");

        ctx.Db.ArcadeSession.Delete(session.Value);
        ctx.Db.ArcadeSession.Insert(new ArcadeSession
        {
            MachineId = machineId, PlayerId = default, GameType = session.Value.GameType,
            State = 0, Bet = 0,
            ChallengeData = correct && !expired ? "CORRECT" : "WRONG",
            ExpiresAt = 0
        });
    }
```

- [ ] **Build and regenerate:**
```bash
cd server && spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
cd ../client && dotnet build SandboxRPG.csproj
```

---

### Task 7.2 — Create ArcadeUI

**Files:**
- Modify: `client/mods/casino/ArcadeUI.cs`

- [ ] **Replace the arcade stub:**

```csharp
#if MOD_CASINO
using Godot;
using System.Collections.Generic;
using SpacetimeDB.Types;

/// <summary>
/// Handles both arcade machine types (Reaction and Pattern).
/// Reaction: renders an animated needle on the machine's SubViewport screen.
/// Pattern: shows colored buttons on the machine surface, player presses in order.
/// </summary>
public partial class ArcadeUI : Node
{
    private static readonly Dictionary<ulong, ArcadeUI> _instances = new();

    private ulong _machineId;
    private bool _isPattern;
    private SubViewport _viewport;
    private CanvasLayer _betPopup;
    private Label _feedbackLabel;
    private bool _isActive;

    // Pattern input state
    private string _currentChallenge = "";
    private string _playerInput = "";

    public static void Open(ulong machineId, bool isPattern)
    {
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowBetPopup();
    }

    public static ArcadeUI AttachToNode(Node3D machineNode, ulong machineId, bool isPattern)
    {
        var ui = new ArcadeUI { _machineId = machineId, _isPattern = isPattern };
        machineNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        _viewport = new SubViewport { Size = new Vector2I(256, 256), TransparentBg = true };
        AddChild(_viewport);

        var bg = new ColorRect
        {
            Color = _isPattern ? new Color(0.15f, 0.05f, 0.25f) : new Color(0.05f, 0.1f, 0.1f),
            Size = new Vector2(256, 256)
        };
        _viewport.AddChild(bg);

        _feedbackLabel = new Label { Position = new Vector2(10, 10) };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 18);
        _viewport.AddChild(_feedbackLabel);

        ApplyScreenTexture();
        GameManager.Instance.Conn.Db.ArcadeSession.OnUpdate += OnSessionUpdate;
    }

    private void ApplyScreenTexture()
    {
        var parent = GetParent<Node3D>();
        if (parent == null) return;
        var screen = parent.FindChild("Screen") as MeshInstance3D;
        if (screen == null) return;
        var mat = new StandardMaterial3D { AlbedoTexture = _viewport.GetTexture() };
        screen.MaterialOverride = mat;
    }

    private void OnSessionUpdate(ArcadeSession old, ArcadeSession newVal, SpacetimeDB.ReducerEvent<SpacetimeDB.Types.Reducer> evt)
    {
        if (newVal.MachineId != _machineId) return;
        CallDeferred(nameof(HandleSessionChange), newVal);
    }

    private void HandleSessionChange(ArcadeSession session)
    {
        var localPlayer = GameManager.Instance.GetLocalPlayer();
        _isActive = session.State == 1 && localPlayer != null && session.PlayerId == localPlayer.Identity;

        if (session.State == 1) // Active — start game
        {
            if (_isPattern) StartPatternGame(session.ChallengeData);
            else StartReactionGame(session);
        }
        else if (session.State == 0) // Result
        {
            _isActive = false;
            bool won = session.ChallengeData is "HIT" or "CORRECT";
            _feedbackLabel.Text = won ? "WIN! 🎉" : "Miss...";
        }
    }

    // ── Reaction game ────────────────────────────────────────────────────────

    private async void StartReactionGame(ArcadeSession session)
    {
        var parts = session.ChallengeData.Split(':');
        int targetMs = int.Parse(parts[0]);

        _feedbackLabel.Text = "Watch the needle...";

        // Draw animated needle on SubViewport
        var needle = new Line2D
        {
            Points = new[] { new Vector2(128, 128), new Vector2(128, 20) },
            Width = 4, DefaultColor = Colors.Red
        };
        _viewport.AddChild(needle);

        // Sweep needle from left to right over targetMs ms
        double elapsed = 0;
        double total = targetMs / 1000.0;
        while (elapsed < total + 1.0) // give 1s extra after target
        {
            float angle = Mathf.Lerp(-70f, 70f, (float)(elapsed / total));
            needle.Rotation = Mathf.DegToRad(angle);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        needle.QueueFree();
        _feedbackLabel.Text = "Too late!";
    }

    public void TriggerReactionInput()
    {
        // Called by player pressing E again while reaction is active
        GameManager.Instance.Conn.Reducers.ArcadeInputReaction(_machineId);
    }

    public static void TryTriggerReaction(ulong machineId)
    {
        if (_instances.TryGetValue(machineId, out var ui) && ui._isActive)
            ui.TriggerReactionInput();
        else
            Open(machineId, isPattern: false);
    }

    // ── Pattern game ─────────────────────────────────────────────────────────

    private async void StartPatternGame(string challenge)
    {
        _currentChallenge = challenge;
        _playerInput = "";

        var colorMap = new Dictionary<char, Color>
        {
            ['R'] = Colors.Red, ['G'] = Colors.Green,
            ['B'] = Colors.Blue, ['Y'] = Colors.Yellow
        };

        // Show sequence one at a time
        for (int i = 0; i < challenge.Length; i++)
        {
            char c = challenge[i];
            _feedbackLabel.Text = $"Remember: {c}";
            var flash = new ColorRect { Color = colorMap[c], Size = new Vector2(256, 200), Position = new Vector2(0, 50) };
            _viewport.AddChild(flash);
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            flash.QueueFree();
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        }
        _feedbackLabel.Text = "Your turn! (R/G/B/Y keys)";
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isPattern || _currentChallenge.Length == 0) return;
        // Pattern input: R/G/B/Y keys
        var keyMap = new Dictionary<Key, char>
        {
            [Key.R] = 'R', [Key.G] = 'G', [Key.B] = 'B', [Key.Y] = 'Y'
        };
        if (@event is InputEventKey keyEvent && keyEvent.Pressed &&
            keyMap.TryGetValue(keyEvent.Keycode, out char pressed))
        {
            _playerInput += pressed;
            _feedbackLabel.Text = $"Input: {_playerInput}";
            if (_playerInput.Length == _currentChallenge.Length)
            {
                GameManager.Instance.Conn.Reducers.ArcadeInputPattern(_machineId, _playerInput);
                _currentChallenge = "";
                _playerInput = "";
            }
        }
    }

    private void ShowBetPopup()
    {
        if (_betPopup != null) { _betPopup.Visible = true; return; }
        _betPopup = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(220, 120);
        panel.Position = new Vector2(-110, -60);
        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = _isPattern ? "Pattern Game — Bet (Copper):" : "Reaction Game — Bet (Copper):" });
        var spin = new SpinBox { MinValue = 1, MaxValue = 5000, Value = 10 };
        vbox.AddChild(spin);
        var btn = new Button { Text = "START" };
        btn.Pressed += () => {
            _betPopup.Visible = false;
            GameManager.Instance.Conn.Reducers.StartArcade(_machineId, (ulong)spin.Value);
        };
        vbox.AddChild(btn);
        panel.AddChild(vbox);
        _betPopup.AddChild(panel);
        GetTree().Root.AddChild(_betPopup);
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
        GameManager.Instance.Conn.Db.ArcadeSession.OnUpdate -= OnSessionUpdate;
    }
}
#endif
```

- [ ] **In WorldManager, add:**
```csharp
#if MOD_CASINO
        if (structureType == "casino_arcade_reaction")
            ArcadeUI.AttachToNode(node, structureId, isPattern: false);
        if (structureType == "casino_arcade_pattern")
            ArcadeUI.AttachToNode(node, structureId, isPattern: true);
#endif
```

- [ ] **Also register the "interact again while playing" for reaction machines. In `InteractionSystem._Input`, after invoking the handler, if the aimed structure is a reaction arcade and a session is active for local player, call `TriggerReactionInput()` instead of `Open()`.**

> Simplification: for v1, re-pressing E on the reaction machine while active triggers the input. The `CasinoUI` registration handles this implicitly since `ArcadeUI.Open()` re-shows the bet popup — update the registration to check session state:

In `CasinoUI.Register()`, update arcade handlers:
```csharp
InteractionSystem.RegisterStructureHandler("casino_arcade_reaction", id => {
    if (_instances.TryGetValue(id, out var ui) && ui._isActive) ui.TriggerReactionInput();
    else ArcadeUI.Open(id, isPattern: false);
});
```

> Add `_isActive` bool to `ArcadeUI`, set true when `State == 1` session is for local player.

- [ ] **Build client:**
```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

> **Smoke test (Reaction):** Press E on reaction machine, enter bet, START — needle sweeps on screen. Press E again at the right moment → WIN or MISS shown, balance updated. **Smoke test (Pattern):** START → sequence flashes on screen → press R/G/B/Y in order → CORRECT or WRONG, balance updated.

---

### Task 7.3 — Commit chunk 7

- [ ] **Final commit:**
```bash
cd C:/Users/Jonas/Documents/GodotGame
git add server/mods/casino/Reducers.cs client/mods/casino/ArcadeUI.cs \
        client/mods/casino/CasinoUI.cs client/scripts/world/WorldManager.cs \
        client/scripts/networking/SpacetimeDB/
git commit -m "feat: arcade machines — StartArcade, ArcadeInputReaction/Pattern, SubViewport animations"
```

---

## End State

After all 7 chunks:

| Feature | Status |
|---|---|
| Mod framework (ModConfig, AdminList, ModManager) | ✅ |
| Currency (CurrencyBalance, ExchangeResources, Withdraw/DepositCoins, CurrencyHUD) | ✅ |
| Casino POI seeded at (50,0,50) | ✅ |
| Slot machine (SpinSlot, reel SubViewport, bet popup) | ✅ |
| Blackjack (full round, 3D cards, multiplayer) | ✅ |
| Coin pusher (PushCoin, RigidBody3D coins, jackpot) | ✅ |
| Arcade reaction + pattern (server-timestamp validation, screen animation) | ✅ |

**Remaining (deferred to model rework completion):**
- Swap placeholder box meshes for final `.glb` models from `client/mods/casino/models/`
- Exchange machine UI (full implementation — stub registered but not built out)
