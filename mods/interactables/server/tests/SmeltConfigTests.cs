// mods/interactables/server/tests/SmeltConfigTests.cs
using NUnit.Framework;
using SandboxRPG.Server;

namespace SandboxRPG.Server.Tests;

[TestFixture]
public class SmeltConfigTests
{
    [SetUp]
    public void SetUp() => SmeltConfig.Clear();

    [Test]
    public void Register_And_Get_ReturnsRecipe()
    {
        SmeltConfig.Register("raw_iron", "iron", 1, 10_000);
        var recipe = SmeltConfig.Get("raw_iron");
        Assert.That(recipe, Is.Not.Null);
        Assert.That(recipe!.Value.OutputItem, Is.EqualTo("iron"));
        Assert.That(recipe.Value.OutputQuantity, Is.EqualTo(1u));
        Assert.That(recipe.Value.DurationMs, Is.EqualTo(10_000UL));
    }

    [Test]
    public void Get_Unregistered_ReturnsNull()
    {
        Assert.That(SmeltConfig.Get("nonexistent"), Is.Null);
    }

    [Test]
    public void Clear_RemovesAllRecipes()
    {
        SmeltConfig.Register("raw_iron", "iron", 1, 10_000);
        SmeltConfig.Clear();
        Assert.That(SmeltConfig.Get("raw_iron"), Is.Null);
    }
}
