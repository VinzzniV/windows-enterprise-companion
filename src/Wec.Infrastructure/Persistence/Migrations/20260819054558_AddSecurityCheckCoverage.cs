using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityCheckCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "coverage_version",
                table: "security_scans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "check_id",
                table: "security_findings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "security_check_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scan_id = table.Column<long>(type: "INTEGER", nullable: false),
                    check_id = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    failure_code = table.Column<string>(type: "TEXT", nullable: true),
                    failure_message = table.Column<string>(type: "TEXT", nullable: true),
                    required_privilege = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_check_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_security_check_results_security_scans_scan_id",
                        column: x => x.scan_id,
                        principalTable: "security_scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_security_check_results_scan_check",
                table: "security_check_results",
                columns: new[] { "scan_id", "check_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "security_check_results");

            migrationBuilder.DropColumn(
                name: "coverage_version",
                table: "security_scans");

            migrationBuilder.DropColumn(
                name: "check_id",
                table: "security_findings");
        }
    }
}
