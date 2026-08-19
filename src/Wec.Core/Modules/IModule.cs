using Microsoft.Extensions.DependencyInjection;

namespace Wec.Core.Modules;

public interface IModule
{
    void RegisterServices(IServiceCollection services);
}
