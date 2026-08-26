using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.PatchManagement.Persistence;

public sealed class WingetManagedPackageRecord
{
    public int Id { get; set; }
    public string OpsiProductId { get; set; } = string.Empty;
    public string WingetId { get; set; } = string.Empty;
    public string Source { get; set; } = "winget";
    public string Scope { get; set; } = "machine";
    public string DepotId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? LastPackagedWingetVersion { get; set; }
    public int TemplateVersion { get; set; } = 1;
    public string? LatestWingetVersion { get; set; }
    public string CheckStatus { get; set; } = "NOT_CHECKED";
    public DateTimeOffset? CheckedAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class WingetManagedPackageRecordConfiguration
    : IEntityTypeConfiguration<WingetManagedPackageRecord>
{
    public void Configure(EntityTypeBuilder<WingetManagedPackageRecord> builder)
    {
        builder.ToTable("patchmanagement_winget_packages");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.OpsiProductId).HasColumnName("opsi_product_id").IsRequired();
        builder.Property(record => record.WingetId).HasColumnName("winget_id").IsRequired();
        builder.Property(record => record.Source).HasColumnName("source").IsRequired();
        builder.Property(record => record.Scope).HasColumnName("scope").IsRequired();
        builder.Property(record => record.DepotId).HasColumnName("depot_id").IsRequired();
        builder.Property(record => record.DisplayName).HasColumnName("display_name").IsRequired();
        builder.Property(record => record.LastPackagedWingetVersion)
            .HasColumnName("last_packaged_winget_version");
        builder.Property(record => record.TemplateVersion).HasColumnName("template_version").IsRequired();
        builder.Property(record => record.LatestWingetVersion).HasColumnName("latest_winget_version");
        builder.Property(record => record.CheckStatus).HasColumnName("check_status").IsRequired();
        ConfigureNullableTimestamp(builder.Property(record => record.CheckedAtUtc), "checked_at_utc");
        builder.Property(record => record.LastError).HasColumnName("last_error");
        ConfigureTimestamp(builder.Property(record => record.CreatedAtUtc), "created_at_utc");
        ConfigureTimestamp(builder.Property(record => record.UpdatedAtUtc), "updated_at_utc");
        builder.HasIndex(record => record.OpsiProductId).IsUnique();
        builder.HasIndex(record => new { record.WingetId, record.DepotId }).IsUnique();
    }

    private static void ConfigureTimestamp(
        PropertyBuilder<DateTimeOffset> property,
        string columnName) => property
        .HasColumnName(columnName)
        .HasConversion(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
        .IsRequired();

    private static void ConfigureNullableTimestamp(
        PropertyBuilder<DateTimeOffset?> property,
        string columnName) => property
        .HasColumnName(columnName)
        .HasConversion(
            value => value.HasValue ? value.Value.UtcTicks : (long?)null,
            ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);
}
