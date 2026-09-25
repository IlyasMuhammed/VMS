using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerRefunds",
                schema: "trp",
                columns: table => new
                {
                    CustomerRefundId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BankCashAccountId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LedgerEntryId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerRefunds", x => x.CustomerRefundId);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceCreditCarryForwards",
                schema: "trp",
                columns: table => new
                {
                    InvoiceCreditCarryForwardId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    TargetInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CarryForwardDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LedgerEntryOutId = table.Column<long>(type: "bigint", nullable: false),
                    LedgerEntryInId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCreditCarryForwards", x => x.InvoiceCreditCarryForwardId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRefunds_TenantId_InvoiceId",
                schema: "trp",
                table: "CustomerRefunds",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCreditCarryForwards_TenantId_SourceInvoiceId",
                schema: "trp",
                table: "InvoiceCreditCarryForwards",
                columns: new[] { "TenantId", "SourceInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCreditCarryForwards_TenantId_TargetInvoiceId",
                schema: "trp",
                table: "InvoiceCreditCarryForwards",
                columns: new[] { "TenantId", "TargetInvoiceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerRefunds",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "InvoiceCreditCarryForwards",
                schema: "trp");
        }
    }
}
