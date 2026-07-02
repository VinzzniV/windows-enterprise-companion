using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.Inventory.Persistence;

public sealed class HardwareSnapshotRecordConfiguration : IEntityTypeConfiguration<HardwareSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<HardwareSnapshotRecord> builder)
    {
        builder.ToTable("inventory_hardware_snapshots");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        // SQLite cannot order/compare DateTimeOffset columns; store UTC ticks instead
        builder.Property(record => record.CapturedAtUtc)
            .HasColumnName("captured_at_utc")
            .HasConversion(
                capturedAt => capturedAt.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();
        builder.Property(record => record.PayloadJson).HasColumnName("payload_json").IsRequired();
        builder.HasIndex(record => record.CapturedAtUtc)
            .HasDatabaseName("ix_inventory_hardware_snapshots_captured_at_utc");
    }
}
