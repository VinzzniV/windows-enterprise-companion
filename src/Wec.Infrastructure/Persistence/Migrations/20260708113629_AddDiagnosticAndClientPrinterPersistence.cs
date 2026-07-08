using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticAndClientPrinterPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diagnostics_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    host = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    completed_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diagnostics_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "printmanagement_client_printer_scans",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    host = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    captured_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printmanagement_client_printer_scans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_diagnostics_runs_host",
                table: "diagnostics_runs",
                column: "host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_printmanagement_client_printer_scans_host",
                table: "printmanagement_client_printer_scans",
                column: "host",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diagnostics_runs");

            migrationBuilder.DropTable(
                name: "printmanagement_client_printer_scans");
        }
    }
}
