using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WecDbContext))]
[Migration("20260814090500_AddPatchVersionSources")]
public sealed class AddPatchVersionSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "patchmanagement_version_sources",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                product_id = table.Column<string>(type: "TEXT", nullable: false),
                source_url = table.Column<string>(type: "TEXT", nullable: false),
                version_pattern = table.Column<string>(type: "TEXT", nullable: false),
                enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                latest_version = table.Column<string>(type: "TEXT", nullable: true),
                last_checked_utc = table.Column<long>(type: "INTEGER", nullable: true),
                check_status = table.Column<string>(type: "TEXT", nullable: false),
                last_error = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_patchmanagement_version_sources", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_patchmanagement_version_sources_product_id",
            table: "patchmanagement_version_sources",
            column: "product_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "patchmanagement_version_sources");
}
