using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceSettlements",
                schema: "trp",
                columns: table => new
                {
                    InvoiceSettlementId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    SettlementNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SettlementType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReversedBy = table.Column<int>(type: "int", nullable: true),
                    ReversedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSettlements", x => x.InvoiceSettlementId);
                });

            migrationBuilder.CreateTable(
                name: "SettlementNumberCounters",
                schema: "trp",
                columns: table => new
                {
                    SettlementNumberCounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementNumberCounters", x => x.SettlementNumberCounterId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSettlements_TenantId_InvoiceId",
                schema: "trp",
                table: "InvoiceSettlements",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementNumberCounters_TenantId_Year",
                schema: "trp",
                table: "SettlementNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSettlements",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "SettlementNumberCounters",
                schema: "trp");
        }
    }
}
