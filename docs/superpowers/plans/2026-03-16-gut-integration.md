# GUT + Server Unit Test Integration Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add GdUnit4 client tests (headless Godot) and NUnit server tests (pure .NET), with per-mod test folders auto-discovered and the `finishing-a-development-branch` skill gating on both suites.

**Architecture:** Server pure logic is extracted into SpacetimeDB-free helpers (`ModLoaderHelpers.cs`, `InventoryHelpers.cs`, `HelloWorldConstants.cs`) so a plain `net8.0` NUnit project can include and test them. Client tests use GdUnit4 as a Godot plugin running headless. Both suites gate the branch-finishing workflow.

**Tech Stack:** NUnit 4.x + Microsoft.NET.Test.Sdk (server), GdUnit4 Godot plugin + C# `[TestSuite]`/`[TestCase]` attributes (client), Godot 4.6.1 headless runner.

> **Framework choice:** This plan uses **GdUnit4** (not the original GUT by Bitwes). GdUnit4 has first-class C# support (`[TestSuite]`/`[TestCase]` attributes, `AssertThat` fluent assertions, `AutoFree` for Node cleanup). GUT is primarily GDScript-based; GdUnit4 is the right choice for an all-C# Godot project.

---

## File Map

| Action | File | Responsibility |
|---|---|---|
| Create | `server/mods/ModLoaderHelpers.cs` | Pure `TopoSort<T>` — no SpacetimeDB |
| Modify | `server/mods/ModLoader.cs` | Delegate to `ModLoaderHelpers.TopoSort` |
| Create | `server/InventoryHelpers.cs` | Pure `ParseIngredients` — no SpacetimeDB |
| Modify | `server/InventoryReducers.cs` | Delegate to `InventoryHelpers.ParseIngredients` |
| Create | `mods/hello-world/server/HelloWorldConstants.cs` | Recipe constants extracted from `Seed()` |
| Modify | `mods/hello-world/server/HelloWorldMod.cs` | Use `HelloWorldConstants.*` in `Seed()` |
| Create | `server/SandboxRPG.Server.Tests/SandboxRPG.Server.Tests.csproj` | NUnit test project targeting `net8.0` |
| Create | `server/SandboxRPG.Server.Tests/ModLoaderTests.cs` | `TopoSort` unit tests |
| Create | `server/SandboxRPG.Server.Tests/InventoryTests.cs` | `ParseIngredients` unit tests |
| Create | `mods/hello-world/server/tests/HelloWorldModTests.cs` | Recipe constants tests |
| Download + commit | `client/addons/gdUnit4/` | GdUnit4 Godot plugin |
| Modify | `client/project.godot` | Enable GdUnit4 plugin |
| Create | `client/tests/TestModManager.cs` | ModManager client tests |
| Create | `client/mods/hello-world/tests/TestHelloWorldClientMod.cs` | HelloWorld client mod tests |
| Modify | `<skill-path>/finishing-a-development-branch/SKILL.md` | Add SandboxRPG test commands to Step 1 |

---

## Chunk 1: Server Refactors + NUnit Test Project

### Task 1: Create `ModLoaderHelpers.cs`

**Files:**
- Create: `server/mods/ModLoaderHelpers.cs`

- [ ] **Step 1: Create the file**

```csharp
// server/mods/ModLoaderHelpers.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG.Server.Mods;

/// <summary>Pure TopoSort with no SpacetimeDB dependency — testable in isolation.</summary>
public static class ModLoaderHelpers
{
    /// <summary>
    /// Topological sort (Kahn's algorithm).
    /// Throws InvalidOperationException on circular dependency.
    /// Unknown dependency names are silently ignored.
    /// </summary>
    public static List<T> TopoSort<T>(
        List<T> items,
        Func<T, string>   getName,
        Func<T, string[]> getDeps)
    {
        var nameToItem = items.ToDictionary(getName);
        var inDegree   = items.ToDictionary(getName, _ => 0);

        foreach (var item in items)
            foreach (var dep in getDeps(item))
                if (nameToItem.ContainsKey(dep))
                    inDegree[getName(item)]++;

        var queue  = new Queue<T>(items.Where(i => inDegree[getName(i)] == 0));
        var result = new List<T>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var dependent in items.Where(i => getDeps(i).Contains(getName(current))))
            {
                inDegree[getName(dependent)]--;
                if (inDegree[getName(dependent)] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count != items.Count)
            throw new InvalidOperationException("[ModLoader] Circular dependency detected in mods.");

        return result;
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd server && dotnet build StdbModule.csproj
```

Expected: 0 errors. The file is included automatically by the existing `<Compile Include>` wildcard or by explicit path — if not, the build will still succeed because `ModLoaderHelpers` is not yet called.

---

### Task 2: Update `ModLoader.cs` to use `ModLoaderHelpers`

**Files:**
- Modify: `server/mods/ModLoader.cs`

- [ ] **Step 1: Replace the file content**

```csharp
// server/mods/ModLoader.cs
using SpacetimeDB;

namespace SandboxRPG.Server.Mods;

public static class ModLoader
{
    private static readonly List<IMod> _mods = new();

    public static void Register(IMod mod) => _mods.Add(mod);

    public static void RunAll(ReducerContext ctx)
    {
        foreach (var mod in ModLoaderHelpers.TopoSort(_mods, m => m.Name, m => m.Dependencies))
        {
            Log.Info($"[ModLoader] Seeding mod: {mod.Name} v{mod.Version}");
            mod.Seed(ctx);
        }
    }
}
```

- [ ] **Step 2: Build server**

```bash
cd server && dotnet build StdbModule.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/mods/ModLoaderHelpers.cs server/mods/ModLoader.cs
git commit -m "refactor: extract ModLoaderHelpers.TopoSort (no SpacetimeDB dep)"
```

---

### Task 3: Create `InventoryHelpers.cs`

**Files:**
- Create: `server/InventoryHelpers.cs`

- [ ] **Step 1: Create the file**

```csharp
// server/InventoryHelpers.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>Pure C# inventory helpers — no SpacetimeDB dependency. Testable in isolation.</summary>
public static class InventoryHelpers
{
    /// <summary>Parses "wood:4,stone:2" ingredient strings into typed tuples.</summary>
    public static List<(string itemType, uint quantity)> ParseIngredients(string? ingredients)
    {
        var result = new List<(string, uint)>();
        if (string.IsNullOrEmpty(ingredients)) return result;

        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2 && uint.TryParse(kv[1], out uint qty))
                result.Add((kv[0].Trim(), qty));
        }
        return result;
    }
}
```

---

### Task 4: Update `InventoryReducers.cs` to delegate to `InventoryHelpers`

**Files:**
- Modify: `server/InventoryReducers.cs` (lines 135–148)

- [ ] **Step 1: Replace the `ParseIngredients` body**

Find the existing `ParseIngredients` method (around line 136) and replace only its body. Keep the signature as `string ingredients` (non-nullable) to match all existing call sites in `CraftingReducers.cs`:

```csharp
/// <summary>Parses "wood:4,stone:2" ingredient strings into typed tuples.</summary>
internal static List<(string itemType, uint quantity)> ParseIngredients(string ingredients)
    => InventoryHelpers.ParseIngredients(ingredients);
```

Passing `string` to `InventoryHelpers.ParseIngredients(string? ingredients)` is valid — no nullability warning.

- [ ] **Step 2: Build server**

```bash
cd server && dotnet build StdbModule.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/InventoryHelpers.cs server/InventoryReducers.cs
git commit -m "refactor: extract InventoryHelpers.ParseIngredients (no SpacetimeDB dep)"
```

---

### Task 5: Extract hello-world recipe constants

**Files:**
- Create: `mods/hello-world/server/HelloWorldConstants.cs`
- Modify: `mods/hello-world/server/HelloWorldMod.cs`

- [ ] **Step 1: Create the constants file**

```csharp
// mods/hello-world/server/HelloWorldConstants.cs
namespace SandboxRPG.Mods.HelloWorld;

/// <summary>
/// Compile-time constants for the hello-world mod recipe.
/// Extracted so they can be verified in unit tests without a ReducerContext.
/// </summary>
public static class HelloWorldConstants
{
    public const string ItemType         = "hello_item";
    public const uint   Quantity         = 1;
    public const string Ingredients      = "wood:1";
    public const float  CraftTimeSeconds = 1f;
}
```

- [ ] **Step 2: Update `HelloWorldMod.cs` to use the constants**

In `mods/hello-world/server/HelloWorldMod.cs`, add `using SandboxRPG.Mods.HelloWorld;` at the top alongside the existing usings.

Then replace the `ctx.Db.CraftingRecipe.Insert(...)` block. The target method is **`HelloWorldModImpl.Seed()`** — the nested private class inside `partial class Module`. The surrounding structure must be preserved:

```csharp
// mods/hello-world/server/HelloWorldMod.cs
using SpacetimeDB;
using System;
using SandboxRPG.Mods.HelloWorld;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly HelloWorldModImpl _helloWorldMod = new();

    private sealed class HelloWorldModImpl : IMod
    {
        public HelloWorldModImpl() => ModLoader.Register(this);

        public string   Name         => "hello-world";
        public string   Version      => "1.0.0";
        public string[] Dependencies => Array.Empty<string>();

        public void Seed(ReducerContext ctx)
        {
            ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
            {
                ResultItemType   = HelloWorldConstants.ItemType,
                ResultQuantity   = HelloWorldConstants.Quantity,
                Ingredients      = HelloWorldConstants.Ingredients,
                CraftTimeSeconds = HelloWorldConstants.CraftTimeSeconds,
            });
            Log.Info("[HelloWorldMod] Seeded.");
        }
    }
}
```

- [ ] **Step 3: Build server**

```bash
cd server && dotnet build StdbModule.csproj
```

Expected: 0 errors.

---

### Task 6: Create the NUnit test project

**Files:**
- Create: `server/SandboxRPG.Server.Tests/SandboxRPG.Server.Tests.csproj`

- [ ] **Step 1: Create the project file**

```xml
<!-- server/SandboxRPG.Server.Tests/SandboxRPG.Server.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>SandboxRPG.Server.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="NUnit" Version="4.*" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.*" />
  </ItemGroup>

  <ItemGroup>
    <!-- Pure helper files only — NOT the SpacetimeDB-dependent originals -->
    <Compile Include="../mods/ModLoaderHelpers.cs" />
    <Compile Include="../InventoryHelpers.cs" />
    <!-- Hello-world constants (no SpacetimeDB) -->
    <Compile Include="../../mods/hello-world/server/HelloWorldConstants.cs" />
    <!-- Per-mod test files (glob picks up all future mods automatically) -->
    <Compile Include="../../mods/hello-world/server/tests/*.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Restore packages**

```bash
cd server/SandboxRPG.Server.Tests && dotnet restore
```

Expected: packages restored, no errors.

- [ ] **Step 3: Commit**

```bash
git add server/SandboxRPG.Server.Tests/SandboxRPG.Server.Tests.csproj
git commit -m "chore: add NUnit server test project skeleton"
```

---

### Task 7: Write `ModLoaderTests.cs`

**Files:**
- Create: `server/SandboxRPG.Server.Tests/ModLoaderTests.cs`

- [ ] **Step 1: Write the failing tests first**

```csharp
// server/SandboxRPG.Server.Tests/ModLoaderTests.cs
using System;
using System.Collections.Generic;
using NUnit.Framework;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server.Tests;

[TestFixture]
public class ModLoaderTests
{
    private record FakeMod(string Name, string[] Dependencies);

    [Test]
    public void TopoSort_NoDependencies_ReturnsAllItems()
    {
        var mods = new List<FakeMod>
        {
            new("a", Array.Empty<string>()),
            new("b", Array.Empty<string>()),
        };

        var result = ModLoaderHelpers.TopoSort(mods, m => m.Name, m => m.Dependencies);

        Assert.That(result.Count, Is.EqualTo(2));
    }

    [Test]
    public void TopoSort_Dependency_ComesBeforeDependent()
    {
        var mods = new List<FakeMod>
        {
            new("shop",     new[] { "currency" }),
            new("currency", Array.Empty<string>()),
        };

        var result = ModLoaderHelpers.TopoSort(mods, m => m.Name, m => m.Dependencies);

        Assert.That(result[0].Name, Is.EqualTo("currency"));
        Assert.That(result[1].Name, Is.EqualTo("shop"));
    }

    [Test]
    public void TopoSort_UnknownDependency_IsIgnoredWithoutThrow()
    {
        var mods = new List<FakeMod>
        {
            new("casino", new[] { "not-registered-mod" }),
        };

        var result = ModLoaderHelpers.TopoSort(mods, m => m.Name, m => m.Dependencies);

        Assert.That(result.Count, Is.EqualTo(1));
    }

    [Test]
    public void TopoSort_CircularDependency_ThrowsInvalidOperation()
    {
        var mods = new List<FakeMod>
        {
            new("a", new[] { "b" }),
            new("b", new[] { "a" }),
        };

        Assert.Throws<InvalidOperationException>(() =>
            ModLoaderHelpers.TopoSort(mods, m => m.Name, m => m.Dependencies));
    }

    [Test]
    public void TopoSort_EmptyList_ReturnsEmpty()
    {
        var result = ModLoaderHelpers.TopoSort(
            new List<FakeMod>(), m => m.Name, m => m.Dependencies);

        Assert.That(result, Is.Empty);
    }
}
```

- [ ] **Step 2: Run tests — expect PASS (implementation already exists)**

```bash
cd server && dotnet test SandboxRPG.Server.Tests/
```

Expected output: `5 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add server/SandboxRPG.Server.Tests/ModLoaderTests.cs
git commit -m "test: add ModLoader TopoSort unit tests"
```

---

### Task 8: Write `InventoryTests.cs`

**Files:**
- Create: `server/SandboxRPG.Server.Tests/InventoryTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// server/SandboxRPG.Server.Tests/InventoryTests.cs
using NUnit.Framework;
using SandboxRPG.Server;

namespace SandboxRPG.Server.Tests;

[TestFixture]
public class InventoryTests
{
    [Test]
    public void ParseIngredients_ValidString_ReturnsTypedTuples()
    {
        var result = InventoryHelpers.ParseIngredients("wood:4,stone:2");

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].itemType, Is.EqualTo("wood"));
        Assert.That(result[0].quantity, Is.EqualTo(4u));
        Assert.That(result[1].itemType, Is.EqualTo("stone"));
        Assert.That(result[1].quantity, Is.EqualTo(2u));
    }

    [Test]
    public void ParseIngredients_EmptyString_ReturnsEmpty()
    {
        Assert.That(InventoryHelpers.ParseIngredients(""), Is.Empty);
    }

    [Test]
    public void ParseIngredients_NullString_ReturnsEmpty()
    {
        Assert.That(InventoryHelpers.ParseIngredients(null), Is.Empty);
    }

    [Test]
    public void ParseIngredients_MalformedEntry_SkipsBadParts()
    {
        // "badentry" has no colon — should be skipped, "wood:4" should be kept
        var result = InventoryHelpers.ParseIngredients("wood:4,badentry");

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].itemType, Is.EqualTo("wood"));
    }

    [Test]
    public void ParseIngredients_SingleIngredient_ReturnsOne()
    {
        var result = InventoryHelpers.ParseIngredients("iron:10");

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].itemType, Is.EqualTo("iron"));
        Assert.That(result[0].quantity, Is.EqualTo(10u));
    }
}
```

- [ ] **Step 2: Run tests**

```bash
cd server && dotnet test SandboxRPG.Server.Tests/
```

Expected: `10 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add server/SandboxRPG.Server.Tests/InventoryTests.cs
git commit -m "test: add ParseIngredients unit tests"
```

---

### Task 9: Write hello-world server mod tests

**Files:**
- Create: `mods/hello-world/server/tests/HelloWorldModTests.cs`

- [ ] **Step 1: Create test file**

```csharp
// mods/hello-world/server/tests/HelloWorldModTests.cs
using NUnit.Framework;
using SandboxRPG.Mods.HelloWorld;

namespace SandboxRPG.Server.Tests;

/// <summary>
/// Verifies hello-world recipe constants are correct.
/// Seed() itself (requires ReducerContext) is not tested here.
/// This establishes the per-mod test pattern for future mods.
/// </summary>
[TestFixture]
public class HelloWorldModTests
{
    [Test]
    public void ItemType_IsHelloItem() =>
        Assert.That(HelloWorldConstants.ItemType, Is.EqualTo("hello_item"));

    [Test]
    public void Quantity_IsOne() =>
        Assert.That(HelloWorldConstants.Quantity, Is.EqualTo(1u));

    [Test]
    public void Ingredients_IsWoodColon1() =>
        Assert.That(HelloWorldConstants.Ingredients, Is.EqualTo("wood:1"));

    [Test]
    public void CraftTimeSeconds_IsOneSec() =>
        Assert.That(HelloWorldConstants.CraftTimeSeconds, Is.EqualTo(1f));
}
```

- [ ] **Step 2: Run all server tests**

```bash
cd server && dotnet test SandboxRPG.Server.Tests/
```

Expected: `14 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add mods/hello-world/server/HelloWorldConstants.cs \
        mods/hello-world/server/HelloWorldMod.cs \
        mods/hello-world/server/tests/HelloWorldModTests.cs
git commit -m "test: add HelloWorld mod constants and per-mod server tests"
```

---

## Chunk 2: GdUnit4 Setup + Client Tests

> GdUnit4 is the Godot 4.x C# unit test framework. It runs inside Godot's runtime, giving tests access to the full Godot API.
> Official repo: https://github.com/MikeSchulze/gdUnit4

### Task 10: Install GdUnit4 addon

**Files:**
- Create: `client/addons/gdUnit4/` (downloaded from GitHub)

- [ ] **Step 1: Find the latest GdUnit4 release compatible with Godot 4.6**

Use WebSearch: `GdUnit4 Godot 4.6 latest release download zip site:github.com`

Find the latest stable release zip from `https://github.com/MikeSchulze/gdUnit4/releases`. The zip file name is typically `gdUnit4-v4.x.x.zip`.

- [ ] **Step 2: Download and extract the addon**

```bash
# Download the release zip (replace VERSION with actual latest, e.g. 4.4.0)
curl -L "https://github.com/MikeSchulze/gdUnit4/releases/download/v<VERSION>/gdUnit4-v<VERSION>.zip" \
     -o /tmp/gdUnit4.zip

# Extract the addons/gdUnit4 folder into client/addons/
cd "C:/Users/Jonas/Documents/GodotGame"
mkdir -p client/addons
unzip /tmp/gdUnit4.zip "addons/gdUnit4/*" -d client/
```

Verify the folder exists:
```bash
ls client/addons/gdUnit4/
```

Expected: contains `plugin.cfg`, `GdUnitCmdTool.gd`, and other GdUnit4 files.

- [ ] **Step 3: Commit the addon**

```bash
git add client/addons/gdUnit4/
git commit -m "chore: add GdUnit4 addon for Godot client tests"
```

---

### Task 11: Enable GdUnit4 plugin in `project.godot`

**Files:**
- Modify: `client/project.godot`

- [ ] **Step 1: Add `[editor_plugins]` section**

Open `client/project.godot`. Find the end of the file and add (if no `[editor_plugins]` section exists):

```ini
[editor_plugins]

enabled=PackedStringArray("res://addons/gdUnit4/plugin.cfg")
```

If an `[editor_plugins]` section already exists, add `"res://addons/gdUnit4/plugin.cfg"` to its `PackedStringArray`.

- [ ] **Step 2: Verify the client still builds**

```bash
cd client && dotnet build SandboxRPG.csproj
```

Expected: 0 errors. (The plugin is Godot-only; the C# build is not affected.)

- [ ] **Step 3: Commit**

```bash
git add client/project.godot
git commit -m "chore: enable GdUnit4 plugin in project.godot"
```

---

### Task 12: Write `TestModManager.cs`

**Files:**
- Create: `client/tests/TestModManager.cs`

GdUnit4 C# test classes use:
- `[TestSuite]` on the class (not `[TestFixture]` — that's NUnit)
- `[TestCase]` on test methods
- `AssertThat(value).IsEqual(expected)` assertions
- `AutoFree(new SomeNode())` to create Nodes that are freed after the test

- [ ] **Step 1: Create test file**

```csharp
// client/tests/TestModManager.cs
using GdUnit4;
using static GdUnit4.Assertions;

namespace SandboxRPG.Tests;

[TestSuite]
public partial class TestModManager
{
    /// <summary>
    /// A minimal IClientMod stub for use in tests.
    /// Tracks whether Initialize() was called.
    /// Note: get-only auto-properties CAN be assigned in C# constructors of the declaring class (C# 6+).
    /// </summary>
    private sealed class StubMod : Godot.Node, IClientMod
    {
        public string ModName { get; }          // assigned in constructor below — valid C# 6+
        public bool   WasInitialized { get; private set; }

        public StubMod(string name) { ModName = name; }

        public void Initialize(Godot.Node sceneRoot) { WasInitialized = true; }
    }

    [TestCase]
    public void Register_DoesNotThrow()
    {
        // Simply call Register — if it throws, GdUnit4 fails the test automatically
        var stub = new StubMod("test-mod");
        ModManager.Register(stub);
    }

    [TestCase]
    public void InitializeAll_CallsInitializeOnRegisteredMod()
    {
        var stub    = new StubMod("test-mod-2");
        var manager = AutoFree(new ModManager());
        var root    = AutoFree(new Godot.Node());

        ModManager.Register(stub);
        manager.InitializeAll(root);

        AssertThat(stub.WasInitialized).IsTrue();
    }
}
```

> **Note on static state:** `ModManager._pending` is a static list — state persists between test cases. Each test uses a unique `ModName` to avoid collisions. When adding new test cases, use unique mod names.

- [ ] **Step 2: Run GdUnit4 headless to confirm the test is discovered and passes**

```bash
"C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" \
  --headless --path client/ \
  -s res://addons/gdUnit4/GdUnitCmdTool.gd \
  -- --add res://tests --exit
```

Expected: test output shows `TestModManager` with 2 tests passing.

---

### Task 13: Write `TestHelloWorldClientMod.cs`

**Files:**
- Create: `client/mods/hello-world/tests/TestHelloWorldClientMod.cs`

- [ ] **Step 1: Create test file**

```csharp
// client/mods/hello-world/tests/TestHelloWorldClientMod.cs
using GdUnit4;
using static GdUnit4.Assertions;
using SandboxRPG;  // for HelloWorldClientMod

namespace SandboxRPG.Tests;

[TestSuite]
public partial class TestHelloWorldClientMod
{
    [TestCase]
    public void ModName_ReturnsHelloWorld()
    {
        var mod = AutoFree(new HelloWorldClientMod());

        AssertThat(mod.ModName).IsEqual("hello-world");
    }
}
```

- [ ] **Step 2: Run GdUnit4 headless across all test directories**

```bash
"C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" \
  --headless --path client/ \
  -s res://addons/gdUnit4/GdUnitCmdTool.gd \
  -- --add res://tests --add res://mods --exit
```

Expected: `TestModManager` (2 tests) + `TestHelloWorldClientMod` (1 test) all pass.

- [ ] **Step 3: Commit**

```bash
git add client/tests/TestModManager.cs \
        client/mods/hello-world/tests/TestHelloWorldClientMod.cs
git commit -m "test: add GdUnit4 client tests for ModManager and HelloWorldClientMod"
```

---

## Chunk 3: `finishing-a-development-branch` Skill Update

### Task 14: Update the skill's Step 1

**Files:**
- Modify: `C:\Users\Jonas\.claude\plugins\cache\claude-plugins-official\superpowers\5.0.1\skills\finishing-a-development-branch\SKILL.md`

The current Step 1 has a generic placeholder. Replace the **entire `### Step 1: Verify Tests` section** with the SandboxRPG-specific version below.

- [ ] **Step 1: Read the current skill file to find the exact text to replace**

Read the file and locate:
```
### Step 1: Verify Tests
```

- [ ] **Step 2: Replace the Step 1 section**

Replace from `### Step 1: Verify Tests` through the line ending with `**If tests pass:** Continue to Step 2.` with the following content (write it directly into the SKILL.md file — do NOT wrap it in a code fence, just the raw markdown text below):

---

    ### Step 1: Verify Tests

    Run both test suites in order. Both must pass before proceeding.

    **1. Server unit tests (pure .NET — no Godot needed):**

        cd server && dotnet test SandboxRPG.Server.Tests/

    **2. GdUnit4 client tests (Godot headless):**

        "C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" --headless --path client/ -s res://addons/gdUnit4/GdUnitCmdTool.gd -- --add res://tests --add res://mods --exit

    **If either fails:**

        Tests failing (<N> failures). Must fix before completing:
        [Show failures]
        Cannot proceed with merge/PR until tests pass.

    Stop. Don't proceed to Step 2.

    **If both pass:** Continue to Step 2.

---

When writing the above into the skill file, use the same markdown formatting as the rest of the skill (backtick fences for code blocks, no indented code blocks). The indented form above is used here only to avoid nested fence conflicts in this plan document.

- [ ] **Step 3: Verify the skill file still reads correctly end-to-end**

Read the modified skill file and confirm:
- Step 1 now has the two SandboxRPG-specific commands
- Steps 2–5 are unchanged
- No formatting is broken

- [ ] **Step 4: Commit**

```bash
git add "C:/Users/Jonas/.claude/plugins/cache/claude-plugins-official/superpowers/5.0.1/skills/finishing-a-development-branch/SKILL.md"
git commit -m "chore: update finishing-a-development-branch skill to run SandboxRPG tests"
```

> **Note:** If the plugin cache directory is not tracked by the project git repo, commit this change separately in the user's home `.claude` repo (if one exists), or skip the commit — the file edit itself is the important step.

---

## Verification: Full Test Run

After all chunks are complete, do a final end-to-end verification:

- [ ] **Run server tests**

```bash
cd server && dotnet test SandboxRPG.Server.Tests/ --verbosity normal
```

Expected: 14+ tests pass, 0 fail.

- [ ] **Run client tests**

```bash
"C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" \
  --headless --path client/ \
  -s res://addons/gdUnit4/GdUnitCmdTool.gd \
  -- --add res://tests --add res://mods --exit
```

Expected: 3+ tests pass (TestModManager ×2, TestHelloWorldClientMod ×1).

- [ ] **Confirm server still builds (WASM)**

```bash
cd server && spacetime build
```

Expected: 0 errors.

---

## Adding Tests for Future Mods

When adding a new mod (e.g. `currency`):

**Server side:** Create `mods/currency/server/tests/CurrencyTests.cs` (NUnit `[TestFixture]`). Add a glob to the `.csproj`:
```xml
<Compile Include="../../mods/currency/server/tests/*.cs" />
```

**Client side:** Create `client/mods/currency/tests/TestCurrencyClientMod.cs` (GdUnit4 `[TestSuite]`). No config change needed — auto-discovered via `--add res://mods`.
