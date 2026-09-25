using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceAdjustments",
                schema: "trp",
                columns: table => new
                {
                    InvoiceAdjustmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    AdjustmentMonth = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReferenceInvoiceNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAdjustments", x => x.InvoiceAdjustmentId);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceHistories",
                schema: "trp",
                columns: table => new
                {
                    InvoiceHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedBy = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceHistories", x => x.InvoiceHistoryId);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                schema: "trp",
                columns: table => new
                {
                    InvoiceLineId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: true),
                    LineType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TripNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TripDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TripType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CustomerTripReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RouteId = table.Column<int>(type: "int", nullable: true),
                    RouteCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RouteLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TripConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    TripCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    VehicleRegNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DriverId = table.Column<int>(type: "int", nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TripRateId = table.Column<long>(type: "bigint", nullable: true),
                    RateEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    RateEffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RateSource = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CustomerCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.InvoiceLineId);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceReplacements",
                schema: "trp",
                columns: table => new
                {
                    InvoiceReplacementId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    NewInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceReplacements", x => x.InvoiceReplacementId);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "trp",
                columns: table => new
                {
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RootInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    PreviousInvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    ReplacedByInvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerInvoiceTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    TemplateVersion = table.Column<int>(type: "int", nullable: true),
                    CustomerBillingAddressId = table.Column<long>(type: "bigint", nullable: true),
                    BillToAddressName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillToAddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillToAddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillToCityId = table.Column<int>(type: "int", nullable: true),
                    BillToProvinceState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillToCountryId = table.Column<int>(type: "int", nullable: true),
                    BillToPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BillToNtn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BillToStrn = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CustomerCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ntn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Strn = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    TotalTripAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAdjustment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDeduction = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdvanceAppliedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WriteOffAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransferredInAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransferredOutAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CarryForwardInAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CarryForwardOutAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BalanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    GeneratedBy = table.Column<int>(type: "int", nullable: true),
                    GeneratedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<int>(type: "int", nullable: true),
                    SubmittedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmissionChannel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CancelledBy = table.Column<int>(type: "int", nullable: true),
                    CancelledOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RegenerationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RegeneratedBy = table.Column<int>(type: "int", nullable: true),
                    RegeneratedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PODRuleApplied = table.Column<bool>(type: "bit", nullable: true),
                    EvidencePageSize = table.Column<int>(type: "int", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.InvoiceId);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceTaxLines",
                schema: "trp",
                columns: table => new
                {
                    InvoiceTaxLineId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerTaxRuleId = table.Column<long>(type: "bigint", nullable: true),
                    TaxName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TaxType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    TaxPercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CalculationBasis = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceTaxLines", x => x.InvoiceTaxLineId);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceTripLinks",
                schema: "trp",
                columns: table => new
                {
                    InvoiceTripLinkId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceLineId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LinkedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UnlinkedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UnlinkReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceTripLinks", x => x.InvoiceTripLinkId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAdjustments_TenantId_InvoiceId",
                schema: "trp",
                table: "InvoiceAdjustments",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHistories_TenantId_InvoiceId",
                schema: "trp",
                table: "InvoiceHistories",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_TenantId_InvoiceId",
                schema: "trp",
                table: "InvoiceLines",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_TenantId_TripId",
                schema: "trp",
                table: "InvoiceLines",
                columns: new[] { "TenantId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReplacements_TenantId_NewInvoiceId",
                schema: "trp",
                table: "InvoiceReplacements",
                columns: new[] { "TenantId", "NewInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReplacements_TenantId_OldInvoiceId",
                schema: "trp",
                table: "InvoiceReplacements",
                columns: new[] { "TenantId", "OldInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_CustomerId_IsActive_PeriodFrom_PeriodTo",
                schema: "trp",
                table: "Invoices",
                columns: new[] { "TenantId", "CustomerId", "IsActive", "PeriodFrom", "PeriodTo" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_InvoiceNumber",
                schema: "trp",
                table: "Invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTaxLines_TenantId_InvoiceId",
                schema: "trp",
                table: "InvoiceTaxLines",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTripLinks_TenantId_InvoiceId",
                schema: "trp",
                table: "InvoiceTripLinks",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTripLinks_TripId",
                schema: "trp",
                table: "InvoiceTripLinks",
                column: "TripId",
                unique: true,
                filter: "[IsActive] = 1");

            // §46.5: "DENY UPDATE, DELETE on ... InvoiceLine to the app role." An INSTEAD OF trigger (the same
            // idiom core.AuditEntries already uses, BR-BP-022) holds whatever account the app connects with,
            // unlike a permission-based DENY tied to one specific SQL login.
            // EXEC: CREATE TRIGGER must start its own batch, which the idempotent script would break.
            migrationBuilder.Sql(@"
EXEC(N'
CREATE TRIGGER [trp].[TR_InvoiceLines_Immutable] ON [trp].[InvoiceLines]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, ''Invoice lines are immutable: they cannot be changed or deleted.'', 1;
END')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [trp].[TR_InvoiceLines_Immutable]");

            migrationBuilder.DropTable(
                name: "InvoiceAdjustments",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "InvoiceHistories",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "InvoiceLines",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "InvoiceReplacements",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "InvoiceTaxLines",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "InvoiceTripLinks",
                schema: "trp");
        }
    }
}
