using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerBalances",
                schema: "trp",
                columns: table => new
                {
                    CustomerBalanceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    BalanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LastEntryId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBalances", x => x.CustomerBalanceId);
                });

            migrationBuilder.CreateTable(
                name: "CustomerLedgerEntries",
                schema: "trp",
                columns: table => new
                {
                    CustomerLedgerEntryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    TripId = table.Column<long>(type: "bigint", nullable: true),
                    EntryType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    ReversesEntryId = table.Column<long>(type: "bigint", nullable: true),
                    DocumentNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Narration = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CustomerSeq = table.Column<long>(type: "bigint", nullable: false),
                    IsSystemGenerated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLedgerEntries", x => x.CustomerLedgerEntryId);
                    table.CheckConstraint("CK_CustomerLedgerEntries_OneOfDebitCredit", "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0)");
                });

            migrationBuilder.CreateTable(
                name: "CustomerLedgerSequenceCounters",
                schema: "trp",
                columns: table => new
                {
                    CustomerLedgerSequenceCounterId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    LastSeq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLedgerSequenceCounters", x => x.CustomerLedgerSequenceCounterId);
                });

            migrationBuilder.CreateTable(
                name: "LedgerNumberCounters",
                schema: "trp",
                columns: table => new
                {
                    LedgerNumberCounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerNumberCounters", x => x.LedgerNumberCounterId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalances_TenantId_CustomerId_CurrencyCode",
                schema: "trp",
                table: "CustomerBalances",
                columns: new[] { "TenantId", "CustomerId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_SourceType_SourceId_EntryType",
                schema: "trp",
                table: "CustomerLedgerEntries",
                columns: new[] { "SourceType", "SourceId", "EntryType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_TenantId_CustomerId_CustomerSeq",
                schema: "trp",
                table: "CustomerLedgerEntries",
                columns: new[] { "TenantId", "CustomerId", "CustomerSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_TenantId_EntryNumber",
                schema: "trp",
                table: "CustomerLedgerEntries",
                columns: new[] { "TenantId", "EntryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_TenantId_InvoiceId",
                schema: "trp",
                table: "CustomerLedgerEntries",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerSequenceCounters_TenantId_CustomerId",
                schema: "trp",
                table: "CustomerLedgerSequenceCounters",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerNumberCounters_TenantId_Year",
                schema: "trp",
                table: "LedgerNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);

            // §40A: "no UPDATE/DELETE grants on the table for the application role" — the same INSTEAD OF
            // UPDATE, DELETE trigger idiom InvoiceLine (CC-23) and core.AuditEntries already use. EXEC: CREATE
            // TRIGGER must start its own batch, which the idempotent script would break.
            migrationBuilder.Sql(@"
EXEC(N'
CREATE TRIGGER [trp].[TR_CustomerLedgerEntries_Immutable] ON [trp].[CustomerLedgerEntries]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, ''Customer ledger entries are immutable: they cannot be changed or deleted.'', 1;
END')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [trp].[TR_CustomerLedgerEntries_Immutable]");

            migrationBuilder.DropTable(
                name: "CustomerBalances",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "CustomerLedgerEntries",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "CustomerLedgerSequenceCounters",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "LedgerNumberCounters",
                schema: "trp");
        }
    }
}
