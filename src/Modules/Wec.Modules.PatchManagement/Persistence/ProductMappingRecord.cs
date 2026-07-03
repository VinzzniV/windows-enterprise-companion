using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wec.Modules.PatchManagement.Persistence;

public sealed class ProductMappingRecord
{
    public int Id { get; set; }

    /// <summary>Inventory display name, matched case-insensitively.</summary>
    public string SoftwareName { get; set; } = string.Empty;

    public string OpsiProductId { get; set; } = string.Empty;
}

public sealed class ProductMappingRecordConfiguration : IEntityTypeConfiguration<ProductMappingRecord>
{
    public void Configure(EntityTypeBuilder<ProductMappingRecord> builder)
    {
        builder.ToTable("patchmanagement_product_mappings");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.SoftwareName).HasColumnName("software_name").IsRequired();
        builder.Property(record => record.OpsiProductId).HasColumnName("opsi_product_id").IsRequired();
        builder.HasIndex(record => record.SoftwareName).IsUnique();
    }
}
