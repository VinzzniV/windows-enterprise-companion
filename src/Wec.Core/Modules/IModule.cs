using Microsoft.Extensions.DependencyInjection;

namespace Wec.Core.Modules;

public interface IModule
{
    ModuleDescriptor Descriptor { get; }

    void RegisterServices(IServiceCollection services);
}
