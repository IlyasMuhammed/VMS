using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Applicable",
                schema: "trp",
                table: "InvoiceTaxLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "InvoiceNumberCounters",
                schema: "trp",
                columns: table => new
                {
                    InvoiceNumberCounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberCounters", x => x.InvoiceNumberCounterId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberCounters_TenantId_Year",
                schema: "trp",
                table: "InvoiceNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceNumberCounters",
                schema: "trp");

            migrationBuilder.DropColumn(
                name: "Applicable",
                schema: "trp",
                table: "InvoiceTaxLines");
        }
    }
}
