// client/mods/currency/tests/TestCurrencyClientMod.cs
using GdUnit4;
using static GdUnit4.Assertions;
using SandboxRPG;

namespace SandboxRPG.Tests;

[TestSuite]
public partial class TestCurrencyClientMod
{
    [TestCase]
    public void ModName_ReturnsCurrency()
    {
        var mod = AutoFree(new CurrencyClientMod());
        AssertThat(mod.ModName).IsEqual("currency");
    }

    [TestCase]
    public void Dependencies_ContainsBase()
    {
        var mod = AutoFree(new CurrencyClientMod());
        AssertThat(mod.Dependencies).Contains("base");
    }
}
