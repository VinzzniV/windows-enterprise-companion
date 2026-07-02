using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_hardware_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_hardware_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_hardware_snapshots_captured_at_utc",
                table: "inventory_hardware_snapshots",
                column: "captured_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_hardware_snapshots");
        }
    }
}
