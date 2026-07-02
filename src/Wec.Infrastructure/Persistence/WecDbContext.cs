using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Wec.Infrastructure.Persistence;

public sealed class WecDbContext : DbContext
{
    private readonly ModelAssemblyRegistry _modelAssemblyRegistry;

    public WecDbContext(DbContextOptions<WecDbContext> options, ModelAssemblyRegistry modelAssemblyRegistry)
        : base(options)
    {
        _modelAssemblyRegistry = modelAssemblyRegistry;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (Assembly moduleAssembly in _modelAssemblyRegistry.Assemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(moduleAssembly);
        }
    }
}
