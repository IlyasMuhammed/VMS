using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankCashAccounts",
                schema: "trp",
                columns: table => new
                {
                    BankCashAccountId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AccountNumberLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankCashAccounts", x => x.BankCashAccountId);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReceipts",
                schema: "trp",
                columns: table => new
                {
                    CustomerReceiptId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ReceiptType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceiptAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BankCashAccountId = table.Column<long>(type: "bigint", nullable: false),
                    InstrumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InstrumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DrawnOnBank = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttachmentDocumentId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceipts", x => x.CustomerReceiptId);
                });

            migrationBuilder.CreateTable(
                name: "InvoicePayments",
                schema: "trp",
                columns: table => new
                {
                    InvoicePaymentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BankCashAccountId = table.Column<long>(type: "bigint", nullable: false),
                    LedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayments", x => x.InvoicePaymentId);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptNumberCounters",
                schema: "trp",
                columns: table => new
                {
                    ReceiptNumberCounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptNumberCounters", x => x.ReceiptNumberCounterId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankCashAccounts_TenantId",
                schema: "trp",
                table: "BankCashAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_TenantId_CustomerId",
                schema: "trp",
                table: "CustomerReceipts",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_TenantId_CustomerId_InstrumentNo",
                schema: "trp",
                table: "CustomerReceipts",
                columns: new[] { "TenantId", "CustomerId", "InstrumentNo" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_TenantId_CustomerReceiptId",
                schema: "trp",
                table: "InvoicePayments",
                columns: new[] { "TenantId", "CustomerReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_TenantId_InvoiceId",
                schema: "trp",
                table: "InvoicePayments",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptNumberCounters_TenantId_Year",
                schema: "trp",
                table: "ReceiptNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankCashAccounts",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "CustomerReceipts",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "InvoicePayments",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "ReceiptNumberCounters",
                schema: "trp");
        }
    }
}
