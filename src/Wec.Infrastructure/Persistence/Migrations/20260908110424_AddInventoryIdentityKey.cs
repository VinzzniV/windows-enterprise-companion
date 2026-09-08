using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryIdentityKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "identity_key",
                table: "inventory_hardware_snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE inventory_hardware_snapshots AS current
                SET identity_key = upper(rtrim(trim(current.host), '.'))
                WHERE trim(current.host) <> ''
                  AND (
                    SELECT COUNT(*)
                    FROM inventory_hardware_snapshots AS candidate
                    WHERE upper(rtrim(trim(candidate.host), '.'))
                        = upper(rtrim(trim(current.host), '.'))
                  ) = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_inventory_hardware_snapshots_identity_key",
                table: "inventory_hardware_snapshots",
                column: "identity_key",
                unique: true,
                filter: "identity_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_inventory_hardware_snapshots_identity_key",
                table: "inventory_hardware_snapshots");

            migrationBuilder.DropColumn(
                name: "identity_key",
                table: "inventory_hardware_snapshots");
        }
    }
}
