// mods/hello-world/server/HelloWorldConstants.cs
namespace SandboxRPG.Mods.HelloWorld;

/// <summary>
/// Compile-time constants for the hello-world mod recipe.
/// Extracted so they can be verified in unit tests without a ReducerContext.
/// </summary>
public static class HelloWorldConstants
{
    public const string ItemType         = "hello_item";
    public const uint   Quantity         = 1;
    public const string Ingredients      = "wood:1";
    public const float  CraftTimeSeconds = 1f;
}
