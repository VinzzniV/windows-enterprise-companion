using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostToSecurityScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "host",
                table: "security_scans",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Pre-remote scans were always local scans of this machine; backfill
            // so the existing scan history stays visible under the local host key.
            // Escaping is not needed: NetBIOS names cannot contain single quotes.
            migrationBuilder.Sql(
                $"UPDATE security_scans SET host = '{Environment.MachineName.ToUpperInvariant()}' WHERE host = ''");

            migrationBuilder.CreateIndex(
                name: "ix_security_scans_host",
                table: "security_scans",
                column: "host");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_security_scans_host",
                table: "security_scans");

            migrationBuilder.DropColumn(
                name: "host",
                table: "security_scans");
        }
    }
}
