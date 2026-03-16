# GUT + Server Unit Test Integration Design

**Date:** 2026-03-16
**Status:** Approved
**Scope:** Integrate GUT (Godot Unit Test) for client-side tests and a .NET NUnit project for server-side pure logic tests. Modify `finishing-a-development-branch` skill to gate on both test suites.

---

## Overview

Two-layer test setup:

1. **Server tests** — `server/SandboxRPG.Server.Tests/` (.NET NUnit, `net8.0`). Tests pure C# logic only: `ModLoader.TopoSort`, `ParseIngredients`, and any extractable mod seed logic. No SpacetimeDB runtime, no WASM.

2. **Client tests** — GUT addon at `client/addons/gut/`. C# test classes (`[TestSuite]` / `[TestCase]`) discovered automatically under `res://tests/` and `res://mods/`. Runs headless via Godot CLI.

Per-mod tests live **inside the mod folder**, discovered automatically by both systems.

The `finishing-a-development-branch` skill Step 1 is updated to run both suites before presenting merge/PR options.

---

## Constraints

- Server project compiles to WASM (`net8.0/wasi-wasm`) — test project targets plain `net8.0` and includes server source via `<Compile Include>` globs. No shared library needed.
- `ReducerContext` is a SpacetimeDB runtime type — cannot be instantiated in unit tests. Mod `Seed()` methods that require `ctx` are tested by extracting pure data constants into a testable static method; the `ctx.Db.Insert` call itself is not tested.
- GUT 4.x uses the `GdUnit4` C# namespace. Test classes are plain C# (not GDScript).
- Godot must be installed to run GUT — already true for this project.

---

## Folder Structure

```
# Server unit test project
server/
└── SandboxRPG.Server.Tests/
    ├── SandboxRPG.Server.Tests.csproj
    ├── ModLoaderTests.cs
    └── InventoryTests.cs

# Server-side per-mod tests (inside mod folder)
mods/
└── hello-world/
    └── server/
        └── tests/
            └── HelloWorldModTests.cs

# GUT addon
client/
├── addons/
│   └── gut/                              (committed to repo)
├── tests/
│   ├── .gutconfig
│   └── TestModManager.cs
└── mods/
    └── hello-world/
        └── tests/
            └── TestHelloWorldClientMod.cs
```

---

## Server Test Project

### `SandboxRPG.Server.Tests.csproj`

Targets `net8.0`. Includes server source files directly via glob. References NUnit + NUnit3TestAdapter.

Key includes:
- `../mods/IMod.cs`, `../ModLoader.cs` (core mod system)
- `../../mods/hello-world/server/**/*.cs` (hello-world mod)
- `../InventoryReducers.cs` (for `ParseIngredients`)

### Test Classes

| File | Tests |
|---|---|
| `ModLoaderTests.cs` | `TopoSort` happy path, unknown deps ignored, circular dep throws `InvalidOperationException` |
| `InventoryTests.cs` | `ParseIngredients` — valid `"wood:4,stone:2"`, empty string, malformed entry |
| `HelloWorldModTests.cs` | Extracted recipe constants have correct values (item type, quantity, ingredients) |

**SpacetimeDB runtime constraint — prerequisite refactors:**

`IMod.cs`, `ModLoader.cs`, and `InventoryReducers.cs` all depend on `SpacetimeDB.Runtime` types (`ReducerContext`, `Log.Info`, `[Reducer]`, `ctx.Db.*`) which cannot compile against plain `net8.0`. Three extractions are required before the test project can compile:

1. **`server/mods/IModCore.cs`** — a runtime-free copy of the `IMod` interface that replaces `ReducerContext` with no parameter (or a testable stub). The actual `IMod` in `mods/IMod.cs` stays unchanged for runtime use; `IModCore` is the test-visible contract.
   *Simpler alternative:* `ModLoader.TopoSort` is a pure static method — extract it into `server/mods/ModLoaderHelpers.cs` as a standalone static class with no runtime imports. Test only `TopoSort`. Leave `RunAll` (which needs `ReducerContext`) untested.

2. **`server/InventoryHelpers.cs`** — extract `ParseIngredients` into a standalone static class with no SpacetimeDB dependency. `InventoryReducers.cs` calls it; the test project includes only `InventoryHelpers.cs`.

The test project includes these extracted files, **not** the originals.

### `.csproj` Package References

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
  <PackageReference Include="NUnit" Version="4.*" />
  <PackageReference Include="NUnit3TestAdapter" Version="4.*" />
</ItemGroup>
```

### Key Source Includes

```xml
<Compile Include="../mods/ModLoaderHelpers.cs" />
<Compile Include="../InventoryHelpers.cs" />
<Compile Include="../../mods/hello-world/server/tests/*.cs" />
```

Note: `IMod.cs`, `ModLoader.cs`, and `InventoryReducers.cs` are **not** included — they depend on `SpacetimeDB.Runtime`. Only the extracted helper files are included.

### Run Command

```bash
cd server && dotnet test SandboxRPG.Server.Tests/
```

---

## GUT Client Setup

### Addon

GUT downloaded from the official GUT GitHub release for Godot 4.x, placed at `client/addons/gut/` and committed. Enable in `project.godot` under `[editor_plugins]` — the exact entry required is:

```ini
[editor_plugins]
enabled=PackedStringArray("res://addons/gut/plugin.cfg")
```

Without this entry the plugin does not activate, and headless test discovery silently finds nothing.

### Discovery Config — `client/tests/.gutconfig`

The `.gutconfig` file must live inside the Godot project root (`client/`) to be addressable as a `res://` path — placing it outside `client/` makes it unreachable to the headless run command.

```json
{
  "dirs": ["res://tests/", "res://mods/"],
  "include_subdirs": true,
  "suffix": "Test*.cs",
  "log_level": 1
}
```

All `Test*.cs` files under `res://tests/` and `res://mods/` (recursively) are discovered. New mod test folders are picked up automatically — no config change needed.

### Test Class Shape (C#)

```csharp
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
public class TestModManager
{
    [Test]
    public void RegisterAddsModToPending()
    {
        // arrange / act / assert
    }
}
```

Note: GUT 4.x C# uses `[Test]` (not `[TestCase]`) for non-parameterized methods. `[TestCase]` is an NUnit attribute for parameterized tests and is not recognized by GUT.

**Static state isolation:** `ModManager._pending` is a `static List<IClientMod>` — state persists between test cases within a session. Tests that call `ModManager.Register` must account for this (e.g. clear state via a test helper or arrange tests to be order-independent).

### Headless Run Command

```bash
"<godot-exe>" --headless --path client/ -s res://addons/gut/gut_cmdln.gd \
  -- -gconfig=res://tests/.gutconfig -gexit
```

Exit code is non-zero on test failure.

Full path (from CLAUDE.md):
```
C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe
```

---

## Per-Mod Test Convention

Each mod that has testable logic adds a `tests/` subfolder:

**Client mod:**
```
client/mods/<mod-name>/tests/Test<ModName>ClientMod.cs
```
Auto-discovered by GUT via `include_subdirs: true` under `res://mods/`.

**Server mod:**
```
mods/<mod-name>/server/tests/<ModName>Tests.cs
```
Included in `SandboxRPG.Server.Tests.csproj` via:
```xml
<Compile Include="../../mods/<mod-name>/server/tests/*.cs" />
```

---

## `finishing-a-development-branch` Skill Modification

Replace the generic test placeholder in **Step 1: Verify Tests** with:

```markdown
### Step 1: Verify Tests

Run both test suites in order:

**1. Server unit tests:**
```bash
cd server && dotnet test SandboxRPG.Server.Tests/
```

**2. GUT client tests (headless):**
```bash
"C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" --headless --path client/ -s res://addons/gut/gut_cmdln.gd -- -gconfig=res://tests/.gutconfig -gexit
```

If either fails: show failures, stop. Do not proceed to Step 2.
```

---

## Out of Scope

- Integration tests against a running SpacetimeDB server
- GDScript tests (all tests are C#)
- Test coverage reporting
- CI/CD pipeline (add when needed)
- Runtime mod loading tests (not possible with WASM)
