// mods/base/server/tests/StructureConfigTests.cs
using NUnit.Framework;
using SandboxRPG.Server;

namespace SandboxRPG.Server.Tests;

[TestFixture]
public class StructureConfigTests
{
    [Test]
    public void Register_And_GetMaxHealth_ReturnsValue()
    {
        StructureConfig.Register("test_wall", 200f);
        Assert.That(StructureConfig.GetMaxHealth("test_wall"), Is.EqualTo(200f));
    }

    [Test]
    public void GetMaxHealth_Unregistered_ReturnsDefault100()
    {
        Assert.That(StructureConfig.GetMaxHealth("unknown_type"), Is.EqualTo(100f));
    }
}
