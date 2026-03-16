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
