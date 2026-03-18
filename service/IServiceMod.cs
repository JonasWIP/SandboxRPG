// service/IServiceMod.cs
namespace SandboxRPG.Service;

public interface IServiceMod
{
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }
    void Initialize(ServiceContext ctx);
}
