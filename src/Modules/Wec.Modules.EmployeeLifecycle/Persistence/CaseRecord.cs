using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Persistence;

public sealed class CaseRecord
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public CaseType Type { get; set; }
    public CaseStatus Status { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public string? Note { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ClosedUtc { get; set; }
}

public sealed class CaseRecordConfiguration : IEntityTypeConfiguration<CaseRecord>
{
    public void Configure(EntityTypeBuilder<CaseRecord> builder)
    {
        builder.ToTable("employee_lifecycle_cases");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(record => record.Type).HasColumnName("type").HasConversion<string>().IsRequired();
        builder.Property(record => record.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(record => record.EffectiveDate).HasColumnName("effective_date");
        builder.Property(record => record.Note).HasColumnName("note");
        builder.Property(record => record.CancelReason).HasColumnName("cancel_reason");
        builder.Property(record => record.CreatedUtc).HasColumnName("created_utc")
            .HasConversion(UtcTicks.Converter).IsRequired();
        builder.Property(record => record.ClosedUtc).HasColumnName("closed_utc")
            .HasConversion(UtcTicks.Converter);
        builder.HasIndex(record => record.EmployeeId);
    }
}
