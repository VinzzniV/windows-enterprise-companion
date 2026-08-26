using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePatchAutomationWithWinget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patchmanagement_product_mappings");

            migrationBuilder.DropTable(
                name: "patchmanagement_version_sources");

            migrationBuilder.CreateTable(
                name: "patchmanagement_winget_packages",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    opsi_product_id = table.Column<string>(type: "TEXT", nullable: false),
                    winget_id = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    scope = table.Column<string>(type: "TEXT", nullable: false),
                    depot_id = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    last_packaged_winget_version = table.Column<string>(type: "TEXT", nullable: true),
                    template_version = table.Column<int>(type: "INTEGER", nullable: false),
                    latest_winget_version = table.Column<string>(type: "TEXT", nullable: true),
                    check_status = table.Column<string>(type: "TEXT", nullable: false),
                    checked_at_utc = table.Column<long>(type: "INTEGER", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patchmanagement_winget_packages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patchmanagement_winget_packages_opsi_product_id",
                table: "patchmanagement_winget_packages",
                column: "opsi_product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patchmanagement_winget_packages_winget_id_depot_id",
                table: "patchmanagement_winget_packages",
                columns: new[] { "winget_id", "depot_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patchmanagement_winget_packages");

            migrationBuilder.CreateTable(
                name: "patchmanagement_product_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    opsi_product_id = table.Column<string>(type: "TEXT", nullable: false),
                    software_name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patchmanagement_product_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patchmanagement_version_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    check_status = table.Column<string>(type: "TEXT", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    last_checked_utc = table.Column<long>(type: "INTEGER", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    latest_version = table.Column<string>(type: "TEXT", nullable: true),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    source_url = table.Column<string>(type: "TEXT", nullable: false),
                    version_pattern = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patchmanagement_version_sources", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patchmanagement_product_mappings_software_name",
                table: "patchmanagement_product_mappings",
                column: "software_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patchmanagement_version_sources_product_id",
                table: "patchmanagement_version_sources",
                column: "product_id",
                unique: true);
        }
    }
}
