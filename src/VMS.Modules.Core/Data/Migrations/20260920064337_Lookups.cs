using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Lookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LookupValues",
                schema: "core",
                columns: table => new
                {
                    LookupValueID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LookupType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Attributes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LookupValues", x => x.LookupValueID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LookupValues_TenantId_LookupType_Code",
                schema: "core",
                table: "LookupValues",
                columns: new[] { "TenantId", "LookupType", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LookupValues_TenantId_LookupType_SortOrder",
                schema: "core",
                table: "LookupValues",
                columns: new[] { "TenantId", "LookupType", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LookupValues",
                schema: "core");
        }
    }
}
