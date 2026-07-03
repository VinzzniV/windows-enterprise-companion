using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatchManagementTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patchmanagement_audit_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    timestamp_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    user_name = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    product_id = table.Column<string>(type: "TEXT", nullable: true),
                    depot_id = table.Column<string>(type: "TEXT", nullable: true),
                    target_clients_json = table.Column<string>(type: "TEXT", nullable: false),
                    preview_json = table.Column<string>(type: "TEXT", nullable: true),
                    result = table.Column<string>(type: "TEXT", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patchmanagement_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patchmanagement_product_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    software_name = table.Column<string>(type: "TEXT", nullable: false),
                    opsi_product_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patchmanagement_product_mappings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patchmanagement_audit_entries_timestamp_utc",
                table: "patchmanagement_audit_entries",
                column: "timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "IX_patchmanagement_product_mappings_software_name",
                table: "patchmanagement_product_mappings",
                column: "software_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patchmanagement_audit_entries");

            migrationBuilder.DropTable(
                name: "patchmanagement_product_mappings");
        }
    }
}
