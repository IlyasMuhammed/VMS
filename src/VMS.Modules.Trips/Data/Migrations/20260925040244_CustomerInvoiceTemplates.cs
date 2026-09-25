using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerInvoiceTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerInvoiceTemplates",
                schema: "trp",
                columns: table => new
                {
                    CustomerInvoiceTemplateId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TemplateType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInvoiceTemplates", x => x.CustomerInvoiceTemplateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoiceTemplates_CustomerId",
                schema: "trp",
                table: "CustomerInvoiceTemplates",
                column: "CustomerId",
                unique: true,
                filter: "[IsDefault] = 1 AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoiceTemplates_CustomerId_TemplateName",
                schema: "trp",
                table: "CustomerInvoiceTemplates",
                columns: new[] { "CustomerId", "TemplateName" },
                unique: true,
                filter: "[EffectiveTo] IS NULL AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoiceTemplates_TenantId_CustomerId",
                schema: "trp",
                table: "CustomerInvoiceTemplates",
                columns: new[] { "TenantId", "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerInvoiceTemplates",
                schema: "trp");
        }
    }
}
