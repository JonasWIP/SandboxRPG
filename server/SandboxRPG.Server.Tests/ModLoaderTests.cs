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
