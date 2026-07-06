using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.Targets.Persistence;

/// <summary>
/// A server/host the admin saves to avoid re-typing it (print server, opsi
/// server, domain controller, a specific client). Stores the host, its role
/// and optionally a user name — NEVER a password (ADR 0007).
/// </summary>
public sealed class SavedTargetRecord
{
    public int Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    /// <summary>One of <see cref="TargetRoles"/>.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Optional saved user name for explicit-credential scans; never a password.</summary>
    public string? UserName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class SavedTargetRecordConfiguration : IEntityTypeConfiguration<SavedTargetRecord>
{
    public void Configure(EntityTypeBuilder<SavedTargetRecord> builder)
    {
        builder.ToTable("targets_saved");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.Label).HasColumnName("label").IsRequired();
        builder.Property(record => record.Host).HasColumnName("host").IsRequired();
        builder.Property(record => record.Role).HasColumnName("role").IsRequired();
        builder.Property(record => record.UserName).HasColumnName("user_name");
        builder.Property(record => record.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(record => new { record.Host, record.Role }).IsUnique();
    }
}
