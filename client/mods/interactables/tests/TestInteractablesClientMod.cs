// client/mods/interactables/tests/TestInteractablesClientMod.cs
using GdUnit4;
using static GdUnit4.Assertions;
using SandboxRPG;

namespace SandboxRPG.Tests;

[TestSuite]
public partial class TestInteractablesClientMod
{
    [TestCase]
    public void ModName_ReturnsInteractables()
    {
        var mod = AutoFree(new InteractablesClientMod());
        AssertThat(mod.ModName).IsEqual("interactables");
    }

    [TestCase]
    public void Dependencies_ContainsBase()
    {
        var mod = AutoFree(new InteractablesClientMod());
        AssertThat(mod.Dependencies).Contains("base");
    }
}
