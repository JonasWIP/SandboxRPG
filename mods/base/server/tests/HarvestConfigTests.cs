// mods/base/server/tests/HarvestConfigTests.cs
using NUnit.Framework;
using SandboxRPG.Server;

namespace SandboxRPG.Server.Tests;

[TestFixture]
public class HarvestConfigTests
{
    [Test]
    public void RegisterDrop_And_GetDrop_ReturnsDrop()
    {
        HarvestConfig.RegisterDrop("test_tree", "wood", 5);
        var (item, qty) = HarvestConfig.GetDrop("test_tree");
        Assert.That(item, Is.EqualTo("wood"));
        Assert.That(qty, Is.EqualTo(5u));
    }

    [Test]
    public void GetDrop_Unregistered_ReturnsDefault()
    {
        var (item, qty) = HarvestConfig.GetDrop("unknown_object");
        Assert.That(item, Is.EqualTo("wood"));
        Assert.That(qty, Is.EqualTo(1u));
    }

    [Test]
    public void RegisterToolDamage_And_Get_ReturnsDamage()
    {
        HarvestConfig.RegisterToolDamage("iron_axe", "tree_pine", 50);
        Assert.That(HarvestConfig.GetToolDamage("iron_axe", "tree_pine"), Is.EqualTo(50u));
    }

    [Test]
    public void GetToolDamage_UnknownTool_ReturnsDefault5()
    {
        Assert.That(HarvestConfig.GetToolDamage("unknown_tool", "tree_pine"), Is.EqualTo(5u));
    }
}
