using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoicePaymentTransfers",
                schema: "trp",
                columns: table => new
                {
                    InvoicePaymentTransferId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    NewInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    TransferNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    AmountTransferred = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransferDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TransferredBy = table.Column<int>(type: "int", nullable: false),
                    TransferredOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LedgerEntryOutId = table.Column<long>(type: "bigint", nullable: false),
                    LedgerEntryInId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePaymentTransfers", x => x.InvoicePaymentTransferId);
                });

            migrationBuilder.CreateTable(
                name: "TransferNumberCounters",
                schema: "trp",
                columns: table => new
                {
                    TransferNumberCounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferNumberCounters", x => x.TransferNumberCounterId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePaymentTransfers_TenantId_NewInvoiceId",
                schema: "trp",
                table: "InvoicePaymentTransfers",
                columns: new[] { "TenantId", "NewInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePaymentTransfers_TenantId_OldInvoiceId",
                schema: "trp",
                table: "InvoicePaymentTransfers",
                columns: new[] { "TenantId", "OldInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferNumberCounters_TenantId_Year",
                schema: "trp",
                table: "TransferNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoicePaymentTransfers",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "TransferNumberCounters",
                schema: "trp");
        }
    }
}
