using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "targets_saved",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    label = table.Column<string>(type: "TEXT", nullable: false),
                    host = table.Column<string>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    user_name = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_targets_saved", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_targets_saved_host_role",
                table: "targets_saved",
                columns: new[] { "host", "role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "targets_saved");
        }
    }
}
