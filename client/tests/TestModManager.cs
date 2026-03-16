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
    private sealed partial class StubMod : Godot.Node, IClientMod
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
