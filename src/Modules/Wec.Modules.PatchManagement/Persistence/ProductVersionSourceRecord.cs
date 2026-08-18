using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.PatchManagement.Persistence;

public sealed class ProductVersionSourceRecord
{
    public int Id { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string VersionPattern { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? LatestVersion { get; set; }
    public DateTimeOffset? LastCheckedUtc { get; set; }
    public string CheckStatus { get; set; } = "NOT_CHECKED";
    public string? LastError { get; set; }
}

public sealed class ProductVersionSourceRecordConfiguration
    : IEntityTypeConfiguration<ProductVersionSourceRecord>
{
    public void Configure(EntityTypeBuilder<ProductVersionSourceRecord> builder)
    {
        builder.ToTable("patchmanagement_version_sources");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(record => record.SourceUrl).HasColumnName("source_url").IsRequired();
        builder.Property(record => record.VersionPattern).HasColumnName("version_pattern").IsRequired();
        builder.Property(record => record.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(record => record.LatestVersion).HasColumnName("latest_version");
        builder.Property(record => record.LastCheckedUtc)
            .HasConversion(
                value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null)
            .HasColumnName("last_checked_utc");
        builder.Property(record => record.CheckStatus).HasColumnName("check_status").IsRequired();
        builder.Property(record => record.LastError).HasColumnName("last_error");
        builder.HasIndex(record => record.ProductId).IsUnique();
    }
}
