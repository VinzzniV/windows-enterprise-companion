using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wec.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeLifecycleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_lifecycle_audit_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    timestamp_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    user_name = table.Column<string>(type: "TEXT", nullable: false),
                    employee_id = table.Column<long>(type: "INTEGER", nullable: false),
                    case_id = table.Column<long>(type: "INTEGER", nullable: true),
                    task_id = table.Column<long>(type: "INTEGER", nullable: true),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    old_value = table.Column<string>(type: "TEXT", nullable: true),
                    new_value = table.Column<string>(type: "TEXT", nullable: true),
                    detail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_lifecycle_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_lifecycle_cases",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    employee_id = table.Column<long>(type: "INTEGER", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    cancel_reason = table.Column<string>(type: "TEXT", nullable: true),
                    created_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    closed_utc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_lifecycle_cases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_lifecycle_employees",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    first_name = table.Column<string>(type: "TEXT", nullable: false),
                    last_name = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    employee_number = table.Column<string>(type: "TEXT", nullable: true),
                    department = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    manager = table.Column<string>(type: "TEXT", nullable: true),
                    sam_account_name = table.Column<string>(type: "TEXT", nullable: true),
                    user_principal_name = table.Column<string>(type: "TEXT", nullable: true),
                    distinguished_name = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    exit_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    created_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_lifecycle_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_lifecycle_tasks",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    case_id = table.Column<long>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    area = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    assignee = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    completed_utc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_lifecycle_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_lifecycle_audit_entries_employee_id",
                table: "employee_lifecycle_audit_entries",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_lifecycle_audit_entries_timestamp_utc",
                table: "employee_lifecycle_audit_entries",
                column: "timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "IX_employee_lifecycle_cases_employee_id",
                table: "employee_lifecycle_cases",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_lifecycle_employees_last_name",
                table: "employee_lifecycle_employees",
                column: "last_name");

            migrationBuilder.CreateIndex(
                name: "IX_employee_lifecycle_tasks_case_id",
                table: "employee_lifecycle_tasks",
                column: "case_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_lifecycle_audit_entries");

            migrationBuilder.DropTable(
                name: "employee_lifecycle_cases");

            migrationBuilder.DropTable(
                name: "employee_lifecycle_employees");

            migrationBuilder.DropTable(
                name: "employee_lifecycle_tasks");
        }
    }
}
