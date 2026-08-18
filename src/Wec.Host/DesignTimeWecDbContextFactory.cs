using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Wec.Infrastructure.Persistence;
using Wec.Modules.Diagnostics.Persistence;
using Wec.Modules.EmployeeLifecycle.Persistence;
using Wec.Modules.Inventory.Persistence;
using Wec.Modules.PatchManagement.Persistence;
using Wec.Modules.PrintManagement.Persistence;
using Wec.Modules.Security.Persistence;
using Wec.Modules.Targets.Persistence;

namespace Wec.Host;

/// <summary>
/// Used only by the dotnet-ef tools:
/// dotnet ef migrations add &lt;Name&gt; --project src/Wec.Infrastructure --startup-project src/Wec.Host
/// </summary>
internal sealed class DesignTimeWecDbContextFactory : IDesignTimeDbContextFactory<WecDbContext>
{
    public WecDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WecDbContext>();
        optionsBuilder.UseSqlite("Data Source=wec-design-time.db");
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
