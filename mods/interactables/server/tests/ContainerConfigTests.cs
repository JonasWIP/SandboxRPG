// mods/interactables/server/tests/ContainerConfigTests.cs
using NUnit.Framework;
using SandboxRPG.Server;

namespace SandboxRPG.Server.Tests;

[TestFixture]
public class ContainerConfigTests
{
    [SetUp]
    public void SetUp() => ContainerConfig.Clear();

    [Test]
    public void Register_And_GetSlotCount_ReturnsRegisteredValue()
    {
        ContainerConfig.Register("chest", 16);
        Assert.That(ContainerConfig.GetSlotCount("chest"), Is.EqualTo(16));
    }

    [Test]
    public void GetSlotCount_Unregistered_ReturnsZero()
    {
        Assert.That(ContainerConfig.GetSlotCount("nonexistent"), Is.EqualTo(0));
    }

    [Test]
    public void Register_Override_UsesLatestValue()
    {
        ContainerConfig.Register("chest", 16);
        ContainerConfig.Register("chest", 32);
        Assert.That(ContainerConfig.GetSlotCount("chest"), Is.EqualTo(32));
    }

    [Test]
    public void Clear_RemovesAllEntries()
    {
        ContainerConfig.Register("chest", 16);
        ContainerConfig.Clear();
        Assert.That(ContainerConfig.GetSlotCount("chest"), Is.EqualTo(0));
    }
}
