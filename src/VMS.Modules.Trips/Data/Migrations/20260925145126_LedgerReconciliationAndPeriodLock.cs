using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class LedgerReconciliationAndPeriodLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerPeriods",
                schema: "trp",
                columns: table => new
                {
                    LedgerPeriodId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YearMonth = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClosedBy = table.Column<int>(type: "int", nullable: true),
                    ClosedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CloseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReopenedBy = table.Column<int>(type: "int", nullable: true),
                    ReopenedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerPeriods", x => x.LedgerPeriodId);
                });

            migrationBuilder.CreateTable(
                name: "LedgerReconciliationMismatches",
                schema: "trp",
                columns: table => new
                {
                    LedgerReconciliationMismatchId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LedgerReconciliationRunId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LedgerBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InvoiceBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerReconciliationMismatches", x => x.LedgerReconciliationMismatchId);
                });

            migrationBuilder.CreateTable(
                name: "LedgerReconciliationRuns",
                schema: "trp",
                columns: table => new
                {
                    LedgerReconciliationRunId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InvoicesChecked = table.Column<int>(type: "int", nullable: false),
                    MismatchCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerReconciliationRuns", x => x.LedgerReconciliationRunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerPeriods_TenantId_YearMonth",
                schema: "trp",
                table: "LedgerPeriods",
                columns: new[] { "TenantId", "YearMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerReconciliationMismatches_TenantId_LedgerReconciliationRunId",
                schema: "trp",
                table: "LedgerReconciliationMismatches",
                columns: new[] { "TenantId", "LedgerReconciliationRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerReconciliationRuns_TenantId_RunOn",
                schema: "trp",
                table: "LedgerReconciliationRuns",
                columns: new[] { "TenantId", "RunOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerPeriods",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "LedgerReconciliationMismatches",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "LedgerReconciliationRuns",
                schema: "trp");
        }
    }
}
