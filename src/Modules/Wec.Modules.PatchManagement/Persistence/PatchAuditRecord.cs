using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.PatchManagement.Persistence;

public sealed class PatchAuditRecord
{
    public long Id { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? ProductId { get; set; }

    public string? DepotId { get; set; }

    /// <summary>JSON array of client ids.</summary>
    public string TargetClientsJson { get; set; } = "[]";

    /// <summary>Serialized preview shown to the admin before the action.</summary>
    public string? PreviewJson { get; set; }

    public string Result { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}

public sealed class PatchAuditRecordConfiguration : IEntityTypeConfiguration<PatchAuditRecord>
{
    public void Configure(EntityTypeBuilder<PatchAuditRecord> builder)
    {
        builder.ToTable("patchmanagement_audit_entries");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        // SQLite cannot order/compare DateTimeOffset columns; store UTC ticks instead
        builder.Property(record => record.TimestampUtc)
            .HasColumnName("timestamp_utc")
            .HasConversion(
                timestamp => timestamp.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
            .IsRequired();
        builder.Property(record => record.UserName).HasColumnName("user_name").IsRequired();
        builder.Property(record => record.Action).HasColumnName("action").IsRequired();
        builder.Property(record => record.ProductId).HasColumnName("product_id");
        builder.Property(record => record.DepotId).HasColumnName("depot_id");
        builder.Property(record => record.TargetClientsJson).HasColumnName("target_clients_json").IsRequired();
        builder.Property(record => record.PreviewJson).HasColumnName("preview_json");
        builder.Property(record => record.Result).HasColumnName("result").IsRequired();
        builder.Property(record => record.ErrorMessage).HasColumnName("error_message");
        builder.HasIndex(record => record.TimestampUtc);
    }
}
