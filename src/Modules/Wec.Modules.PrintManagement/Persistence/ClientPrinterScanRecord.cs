using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.PrintManagement.Persistence;

/// <summary>Latest installed-printer scan per client (host), stored as a JSON blob.</summary>
public sealed class ClientPrinterScanRecord
{
    public long Id { get; set; }

    public string Host { get; set; } = string.Empty;

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class ClientPrinterScanRecordConfiguration : IEntityTypeConfiguration<ClientPrinterScanRecord>
{
    public void Configure(EntityTypeBuilder<ClientPrinterScanRecord> builder)
    {
        builder.ToTable("printmanagement_client_printer_scans");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.Host)
            .HasColumnName("host")
            .HasDefaultValue(string.Empty)
            .IsRequired();
        // SQLite cannot order/compare DateTimeOffset columns; store UTC ticks instead
        builder.Property(record => record.CapturedAtUtc)
            .HasColumnName("captured_at_utc")
            .HasConversion(
                capturedAt => capturedAt.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();
        builder.Property(record => record.PayloadJson).HasColumnName("payload_json").IsRequired();
        builder.HasIndex(record => record.Host)
            .IsUnique()
            .HasDatabaseName("ix_printmanagement_client_printer_scans_host");
    }
}
