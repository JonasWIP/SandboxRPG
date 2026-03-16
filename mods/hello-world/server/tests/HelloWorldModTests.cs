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
