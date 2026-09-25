using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdvanceNumberCounters",
                schema: "trp",
                columns: table => new
                {
                    AdvanceNumberCounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceNumberCounters", x => x.AdvanceNumberCounterId);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAdvances",
                schema: "trp",
                columns: table => new
                {
                    CustomerAdvanceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdvanceNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    AdvanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MovedFromTripId = table.Column<long>(type: "bigint", nullable: true),
                    MoveReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MovedBy = table.Column<int>(type: "int", nullable: true),
                    MovedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefundedBy = table.Column<int>(type: "int", nullable: true),
                    RefundedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundLedgerEntryId = table.Column<long>(type: "bigint", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReversedBy = table.Column<int>(type: "int", nullable: true),
                    ReversedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAdvances", x => x.CustomerAdvanceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceNumberCounters_TenantId_Year",
                schema: "trp",
                table: "AdvanceNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAdvances_TenantId_CustomerId",
                schema: "trp",
                table: "CustomerAdvances",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAdvances_TenantId_TripId",
                schema: "trp",
                table: "CustomerAdvances",
                columns: new[] { "TenantId", "TripId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvanceNumberCounters",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "CustomerAdvances",
                schema: "trp");
        }
    }
}
