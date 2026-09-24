using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Vehicles.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecurringCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleRecurringCharges",
                schema: "veh",
                columns: table => new
                {
                    VehicleRecurringChargeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeTypeId = table.Column<int>(type: "int", nullable: false),
                    PayeeId = table.Column<int>(type: "int", nullable: false),
                    ExpenseTypeId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AmountBasis = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DueDay = table.Column<int>(type: "int", nullable: true),
                    DueMonth = table.Column<int>(type: "int", nullable: true),
                    CustomIntervalDays = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: true),
                    GeneratedCount = table.Column<int>(type: "int", nullable: false),
                    PostingMode = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    GenerateLeadDays = table.Column<int>(type: "int", nullable: false),
                    TaxWithholdingPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    NextDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleRecurringCharges", x => x.VehicleRecurringChargeId);
                    table.ForeignKey(
                        name: "FK_VehicleRecurringCharges_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleRecurringChargeEntries",
                schema: "veh",
                columns: table => new
                {
                    VehicleRecurringChargeEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    VehicleRecurringChargeId = table.Column<int>(type: "int", nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentMode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TransactionId = table.Column<int>(type: "int", nullable: true),
                    WaiveReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConfirmedBy = table.Column<int>(type: "int", nullable: true),
                    ConfirmedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleRecurringChargeEntries", x => x.VehicleRecurringChargeEntryId);
                    table.ForeignKey(
                        name: "FK_VehicleRecurringChargeEntries_VehicleRecurringCharges_VehicleRecurringChargeId",
                        column: x => x.VehicleRecurringChargeId,
                        principalSchema: "veh",
                        principalTable: "VehicleRecurringCharges",
                        principalColumn: "VehicleRecurringChargeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleRecurringChargeEntries_VehicleTransactions_TransactionId",
                        column: x => x.TransactionId,
                        principalSchema: "veh",
                        principalTable: "VehicleTransactions",
                        principalColumn: "VehicleTransactionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRecurringChargeEntries_TenantId_VehicleId_Status_DueDate",
                schema: "veh",
                table: "VehicleRecurringChargeEntries",
                columns: new[] { "TenantId", "VehicleId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRecurringChargeEntries_TransactionId",
                schema: "veh",
                table: "VehicleRecurringChargeEntries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UX_VehicleRecurringChargeEntries_Period",
                schema: "veh",
                table: "VehicleRecurringChargeEntries",
                columns: new[] { "VehicleRecurringChargeId", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRecurringCharges_TenantId_NextDueDate",
                schema: "veh",
                table: "VehicleRecurringCharges",
                columns: new[] { "TenantId", "NextDueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRecurringCharges_TenantId_VehicleId_EffectiveTo",
                schema: "veh",
                table: "VehicleRecurringCharges",
                columns: new[] { "TenantId", "VehicleId", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRecurringCharges_VehicleId",
                schema: "veh",
                table: "VehicleRecurringCharges",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "UX_VehicleRecurringCharges_Open",
                schema: "veh",
                table: "VehicleRecurringCharges",
                columns: new[] { "TenantId", "SeriesId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleRecurringChargeEntries",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "VehicleRecurringCharges",
                schema: "veh");
        }
    }
}
