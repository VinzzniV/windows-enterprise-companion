using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostToHardwareSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "host",
                table: "inventory_hardware_snapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_hardware_snapshots_host",
                table: "inventory_hardware_snapshots",
                column: "host");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_hardware_snapshots_host",
                table: "inventory_hardware_snapshots");

            migrationBuilder.DropColumn(
                name: "host",
                table: "inventory_hardware_snapshots");
        }
    }
}
