using Microsoft.EntityFrameworkCore;
using Wec.Infrastructure.Persistence;
using Wec.Modules.Diagnostics.Persistence;
using Wec.Modules.EmployeeLifecycle.Persistence;
using Wec.Modules.Inventory.Persistence;
using Wec.Modules.PatchManagement.Persistence;
using Wec.Modules.PrintManagement.Persistence;
using Wec.Modules.Security.Persistence;
using Wec.Modules.Targets.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Builds the context with ALL module assemblies, exactly like the host.
/// EF Core 9 fails Migrate() when the runtime model differs from the migration
/// snapshot (PendingModelChangesWarning), so a partial model is not an option.
/// New module ⇒ add its assembly here.
/// </summary>
internal static class IntegrationDbContextFactory
{
    public static WecDbContext Create(string databasePath)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WecDbContext>();
        optionsBuilder.UseSqlite($"Data Source={databasePath}");
        return new WecDbContext(
            optionsBuilder.Options,
            new ModelAssemblyRegistry([
                typeof(HardwareSnapshotRecord).Assembly,
                typeof(SecurityScanRecord).Assembly,
                typeof(PatchAuditRecord).Assembly,
                typeof(PrintSnapshotRecord).Assembly,
                typeof(SavedTargetRecord).Assembly,
                typeof(DiagnosticRunRecord).Assembly,
                typeof(EmployeeRecord).Assembly,
            ]));
    }
}
