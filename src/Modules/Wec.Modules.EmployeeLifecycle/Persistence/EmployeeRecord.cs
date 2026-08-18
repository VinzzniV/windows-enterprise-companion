using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Persistence;

public sealed class EmployeeRecord
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? EmployeeNumber { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? Manager { get; set; }
    public string? SamAccountName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? DistinguishedName { get; set; }
    public EmployeeStatus Status { get; set; }
    public DateOnly? EntryDate { get; set; }
    public DateOnly? ExitDate { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class EmployeeRecordConfiguration : IEntityTypeConfiguration<EmployeeRecord>
{
    public void Configure(EntityTypeBuilder<EmployeeRecord> builder)
    {
        builder.ToTable("employee_lifecycle_employees");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.FirstName).HasColumnName("first_name").IsRequired();
        builder.Property(record => record.LastName).HasColumnName("last_name").IsRequired();
        builder.Property(record => record.Email).HasColumnName("email");
        builder.Property(record => record.EmployeeNumber).HasColumnName("employee_number");
        builder.Property(record => record.Department).HasColumnName("department");
        builder.Property(record => record.Title).HasColumnName("title");
        builder.Property(record => record.Manager).HasColumnName("manager");
        builder.Property(record => record.SamAccountName).HasColumnName("sam_account_name");
        builder.Property(record => record.UserPrincipalName).HasColumnName("user_principal_name");
        builder.Property(record => record.DistinguishedName).HasColumnName("distinguished_name");
        builder.Property(record => record.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(record => record.EntryDate).HasColumnName("entry_date");
        builder.Property(record => record.ExitDate).HasColumnName("exit_date");
        builder.Property(record => record.Notes).HasColumnName("notes");
        builder.Property(record => record.CreatedUtc).HasColumnName("created_utc")
            .HasConversion(UtcTicks.Converter).IsRequired();
        builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc")
            .HasConversion(UtcTicks.Converter).IsRequired();
        builder.HasIndex(record => record.LastName);
    }
}
