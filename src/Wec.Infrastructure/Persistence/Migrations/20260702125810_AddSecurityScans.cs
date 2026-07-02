using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "security_scans",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    started_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    completed_at_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    finding_count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_scans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "security_findings",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scan_id = table.Column<long>(type: "INTEGER", nullable: false),
                    finding_id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    severity = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: false),
                    affected_resource = table.Column<string>(type: "TEXT", nullable: false),
                    evidence_json = table.Column<string>(type: "TEXT", nullable: false),
                    recommendation = table.Column<string>(type: "TEXT", nullable: false),
                    required_privilege = table.Column<string>(type: "TEXT", nullable: true),
                    captured_at_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_findings", x => x.id);
                    table.ForeignKey(
                        name: "FK_security_findings_security_scans_scan_id",
                        column: x => x.scan_id,
                        principalTable: "security_scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_security_findings_scan_id",
                table: "security_findings",
                column: "scan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "security_findings");

            migrationBuilder.DropTable(
                name: "security_scans");
        }
    }
}
