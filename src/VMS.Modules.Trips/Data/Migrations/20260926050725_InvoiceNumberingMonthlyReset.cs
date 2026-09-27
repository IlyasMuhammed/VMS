using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceNumberingMonthlyReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceNumberCounters_TenantId_Year",
                schema: "trp",
                table: "InvoiceNumberCounters");

            migrationBuilder.AddColumn<int>(
                name: "Month",
                schema: "trp",
                table: "InvoiceNumberCounters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberCounters_TenantId_Year_Month",
                schema: "trp",
                table: "InvoiceNumberCounters",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceNumberCounters_TenantId_Year_Month",
                schema: "trp",
                table: "InvoiceNumberCounters");

            migrationBuilder.DropColumn(
                name: "Month",
                schema: "trp",
                table: "InvoiceNumberCounters");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberCounters_TenantId_Year",
                schema: "trp",
                table: "InvoiceNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);
        }
    }
}
