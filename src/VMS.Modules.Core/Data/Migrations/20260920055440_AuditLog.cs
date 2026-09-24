using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "core",
                columns: table => new
                {
                    AuditEntryID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequiredPermission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.AuditEntryID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_GroupId",
                schema: "core",
                table: "AuditEntries",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_Entity_RecordId_OccurredAt",
                schema: "core",
                table: "AuditEntries",
                columns: new[] { "TenantId", "Entity", "RecordId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_OccurredAt",
                schema: "core",
                table: "AuditEntries",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_UserId_OccurredAt",
                schema: "core",
                table: "AuditEntries",
                columns: new[] { "TenantId", "UserId", "OccurredAt" });

            // BR-BP-022: no role, including Admin, can edit or delete audit rows. The database refuses it
            // outright, so it holds for code that bypasses the application too. An INSTEAD OF trigger
            // (rather than a permission) works whatever account the application connects with.
            // EXEC: CREATE TRIGGER must start its own batch, which the idempotent script would break.
            migrationBuilder.Sql(@"
EXEC(N'
CREATE TRIGGER [core].[TR_AuditEntries_AppendOnly] ON [core].[AuditEntries]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, ''Audit entries are append-only: they cannot be changed or deleted.'', 1;
END')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [core].[TR_AuditEntries_AppendOnly]");

            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "core");
        }
    }
}
