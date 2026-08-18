using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.EmployeeLifecycle.Persistence;

/// <summary>
/// Department catalog: drives the department dropdown and the AD derivations
/// (manager autofill, OU-based DN/UPN suggestions, OU user listing).
/// </summary>
public sealed class DepartmentRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    public string? OuDistinguishedName { get; set; }
}

public sealed class DepartmentRecordConfiguration : IEntityTypeConfiguration<DepartmentRecord>
{
    public void Configure(EntityTypeBuilder<DepartmentRecord> builder)
    {
        builder.ToTable("employee_lifecycle_departments");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.Name).HasColumnName("name").IsRequired();
        builder.Property(record => record.ManagerName).HasColumnName("manager_name");
        builder.Property(record => record.OuDistinguishedName).HasColumnName("ou_distinguished_name");
        builder.HasIndex(record => record.Name).IsUnique();
    }
}
