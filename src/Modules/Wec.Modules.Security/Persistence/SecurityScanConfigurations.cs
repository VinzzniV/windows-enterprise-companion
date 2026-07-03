using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.Security.Persistence;

public sealed class SecurityScanRecordConfiguration : IEntityTypeConfiguration<SecurityScanRecord>
{
    public void Configure(EntityTypeBuilder<SecurityScanRecord> builder)
    {
        builder.ToTable("security_scans");
        builder.HasKey(scan => scan.Id);
        builder.Property(scan => scan.Id).HasColumnName("id");
        builder.Property(scan => scan.Host)
            .HasColumnName("host")
            .HasDefaultValue(string.Empty)
            .IsRequired();
        builder.HasIndex(scan => scan.Host).HasDatabaseName("ix_security_scans_host");
        // SQLite cannot order/compare DateTimeOffset columns; store UTC ticks
        builder.Property(scan => scan.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .HasConversion(startedAt => startedAt.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();
        builder.Property(scan => scan.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasConversion(completedAt => completedAt.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();
        builder.Property(scan => scan.Status).HasColumnName("status").IsRequired();
        builder.Property(scan => scan.FindingCount).HasColumnName("finding_count").IsRequired();

        builder.HasMany(scan => scan.Findings)
            .WithOne()
            .HasForeignKey(finding => finding.ScanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SecurityFindingRecordConfiguration : IEntityTypeConfiguration<SecurityFindingRecord>
{
    public void Configure(EntityTypeBuilder<SecurityFindingRecord> builder)
    {
        builder.ToTable("security_findings");
        builder.HasKey(finding => finding.Id);
        builder.Property(finding => finding.Id).HasColumnName("id");
        builder.Property(finding => finding.ScanId).HasColumnName("scan_id").IsRequired();
        builder.Property(finding => finding.FindingId).HasColumnName("finding_id").IsRequired();
        builder.Property(finding => finding.Title).HasColumnName("title").IsRequired();
        builder.Property(finding => finding.Description).HasColumnName("description").IsRequired();
        builder.Property(finding => finding.Severity).HasColumnName("severity").IsRequired();
        builder.Property(finding => finding.Category).HasColumnName("category").IsRequired();
        builder.Property(finding => finding.AffectedResource).HasColumnName("affected_resource").IsRequired();
        builder.Property(finding => finding.EvidenceJson).HasColumnName("evidence_json").IsRequired();
        builder.Property(finding => finding.Recommendation).HasColumnName("recommendation").IsRequired();
        builder.Property(finding => finding.RequiredPrivilege).HasColumnName("required_privilege");
        builder.Property(finding => finding.CapturedAtUtc)
            .HasColumnName("captured_at_utc")
            .HasConversion(capturedAt => capturedAt.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();

        builder.HasIndex(finding => finding.ScanId).HasDatabaseName("ix_security_findings_scan_id");
    }
}
