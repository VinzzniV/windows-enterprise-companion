using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.EmployeeLifecycle.Persistence;

public sealed class LifecycleAuditRecord
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string UserName { get; set; } = string.Empty;
    public long EmployeeId { get; set; }
    public long? CaseId { get; set; }
    public long? TaskId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Detail { get; set; }
}

public sealed class LifecycleAuditRecordConfiguration : IEntityTypeConfiguration<LifecycleAuditRecord>
{
    public void Configure(EntityTypeBuilder<LifecycleAuditRecord> builder)
    {
        builder.ToTable("employee_lifecycle_audit_entries");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.TimestampUtc).HasColumnName("timestamp_utc")
            .HasConversion(UtcTicks.Converter).IsRequired();
        builder.Property(record => record.UserName).HasColumnName("user_name").IsRequired();
        builder.Property(record => record.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(record => record.CaseId).HasColumnName("case_id");
        builder.Property(record => record.TaskId).HasColumnName("task_id");
        builder.Property(record => record.EventType).HasColumnName("event_type").IsRequired();
        builder.Property(record => record.OldValue).HasColumnName("old_value");
        builder.Property(record => record.NewValue).HasColumnName("new_value");
        builder.Property(record => record.Detail).HasColumnName("detail");
        builder.HasIndex(record => record.EmployeeId);
        builder.HasIndex(record => record.TimestampUtc);
    }
}
