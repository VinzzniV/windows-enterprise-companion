using System.Reflection;

namespace Wec.Infrastructure.Persistence;

/// <summary>
/// Assemblies whose <c>IEntityTypeConfiguration</c> implementations shape the model.
/// The host builds this from its module list so Infrastructure never references modules.
/// </summary>
public sealed class ModelAssemblyRegistry
{
    public ModelAssemblyRegistry(IReadOnlyList<Assembly> assemblies)
    {
        Assemblies = assemblies;
    }

    public IReadOnlyList<Assembly> Assemblies { get; }
}
