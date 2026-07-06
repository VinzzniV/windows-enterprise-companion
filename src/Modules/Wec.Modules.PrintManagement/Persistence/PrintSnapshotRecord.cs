using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.PrintManagement.Persistence;

public sealed class PrintSnapshotRecord
{
    public long Id { get; set; }

    public string Server { get; set; } = string.Empty;

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class PrintSnapshotRecordConfiguration : IEntityTypeConfiguration<PrintSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<PrintSnapshotRecord> builder)
    {
        builder.ToTable("printmanagement_snapshots");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.Server).HasColumnName("server").IsRequired();
        // SQLite cannot order/compare DateTimeOffset columns; store UTC ticks instead
        builder.Property(record => record.CapturedAtUtc)
            .HasColumnName("captured_at_utc")
            .HasConversion(
                capturedAt => capturedAt.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();
        builder.Property(record => record.PayloadJson).HasColumnName("payload_json").IsRequired();
        builder.HasIndex(record => new { record.Server, record.CapturedAtUtc });
    }
}
