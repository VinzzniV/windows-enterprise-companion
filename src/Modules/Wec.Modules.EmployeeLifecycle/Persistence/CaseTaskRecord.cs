using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Persistence;

public sealed class CaseTaskRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public TaskArea Area { get; set; }
    public LifecycleTaskStatus Status { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Assignee { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class CaseTaskRecordConfiguration : IEntityTypeConfiguration<CaseTaskRecord>
{
    public void Configure(EntityTypeBuilder<CaseTaskRecord> builder)
    {
        builder.ToTable("employee_lifecycle_tasks");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.CaseId).HasColumnName("case_id").IsRequired();
        builder.Property(record => record.Title).HasColumnName("title").IsRequired();
        builder.Property(record => record.Area).HasColumnName("area").HasConversion<string>().IsRequired();
        builder.Property(record => record.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(record => record.DueDate).HasColumnName("due_date");
        builder.Property(record => record.Assignee).HasColumnName("assignee");
        builder.Property(record => record.Notes).HasColumnName("notes").IsRequired();
        builder.Property(record => record.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(record => record.CreatedUtc).HasColumnName("created_utc")
            .HasConversion(UtcTicks.Converter).IsRequired();
        builder.Property(record => record.CompletedUtc).HasColumnName("completed_utc")
            .HasConversion(UtcTicks.Converter);
        builder.HasIndex(record => record.CaseId);
    }
}
