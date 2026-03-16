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
