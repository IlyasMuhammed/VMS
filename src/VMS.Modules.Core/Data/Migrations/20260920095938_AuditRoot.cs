using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RootEntity",
                schema: "core",
                table: "AuditEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RootRecordId",
                schema: "core",
                table: "AuditEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_RootEntity_RootRecordId_OccurredAt",
                schema: "core",
                table: "AuditEntries",
                columns: new[] { "TenantId", "RootEntity", "RootRecordId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_TenantId_RootEntity_RootRecordId_OccurredAt",
                schema: "core",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "RootEntity",
                schema: "core",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "RootRecordId",
                schema: "core",
                table: "AuditEntries");
        }
    }
}
