using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.Diagnostics.Persistence;

/// <summary>Latest diagnostic run per host, stored as a JSON blob.</summary>
public sealed class DiagnosticRunRecord
{
    public long Id { get; set; }

    public string Host { get; set; } = string.Empty;

    public DateTimeOffset CompletedAtUtc { get; set; }

    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class DiagnosticRunRecordConfiguration : IEntityTypeConfiguration<DiagnosticRunRecord>
{
    public void Configure(EntityTypeBuilder<DiagnosticRunRecord> builder)
    {
        builder.ToTable("diagnostics_runs");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.Host)
            .HasColumnName("host")
            .HasDefaultValue(string.Empty)
            .IsRequired();
        // SQLite cannot order/compare DateTimeOffset columns; store UTC ticks instead
        builder.Property(record => record.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasConversion(
                completedAt => completedAt.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();
        builder.Property(record => record.PayloadJson).HasColumnName("payload_json").IsRequired();
        builder.HasIndex(record => record.Host)
            .IsUnique()
            .HasDatabaseName("ix_diagnostics_runs_host");
    }
}
