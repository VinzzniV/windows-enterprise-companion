using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WecDbContext))]
[Migration("20260814131500_AddPatchAuditVersions")]
public sealed class AddPatchAuditVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "new_version",
            table: "patchmanagement_audit_entries",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "old_version",
            table: "patchmanagement_audit_entries",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "new_version", table: "patchmanagement_audit_entries");
        migrationBuilder.DropColumn(name: "old_version", table: "patchmanagement_audit_entries");
    }
}
