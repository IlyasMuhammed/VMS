using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Vehicles.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "veh");

            migrationBuilder.CreateTable(
                name: "Vehicles",
                schema: "veh",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegistrationNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegNoKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegistrationCityId = table.Column<int>(type: "int", nullable: true),
                    ChassisNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    EngineNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VehicleTypeId = table.Column<int>(type: "int", nullable: false),
                    MakeId = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ManufacturingYear = table.Column<int>(type: "int", nullable: true),
                    Colour = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FuelType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TankCapacity = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    LoadCapacity = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    CapacityUnit = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    AxleConfigurationId = table.Column<int>(type: "int", nullable: true),
                    BodyTypeId = table.Column<int>(type: "int", nullable: true),
                    TyreCount = table.Column<int>(type: "int", nullable: true),
                    Gvw = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpeningOdometer = table.Column<int>(type: "int", nullable: true),
                    OpeningOdometerDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DefaultDriverId = table.Column<int>(type: "int", nullable: true),
                    FuelCardCompanyId = table.Column<int>(type: "int", nullable: true),
                    FuelCardNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TrackerCompanyId = table.Column<int>(type: "int", nullable: true),
                    TrackerDeviceId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AcquisitionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AcquisitionType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CurrentCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CurrentCounterpartyId = table.Column<int>(type: "int", nullable: true),
                    DraftData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsLive = table.Column<bool>(type: "bit", nullable: false, computedColumnSql: "CAST(CASE WHEN [Status] IN ('Sold', 'Transferred') OR [IsDeleted] = 1 THEN 0 ELSE 1 END AS bit)", stored: true),
                    IsInFleet = table.Column<bool>(type: "bit", nullable: false, computedColumnSql: "CAST(CASE WHEN [Status] IN ('Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable') AND [IsDeleted] = 0 THEN 1 ELSE 0 END AS bit)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.VehicleId);
                });

            migrationBuilder.CreateTable(
                name: "DriverAssignments",
                schema: "veh",
                columns: table => new
                {
                    DriverAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverAssignments", x => x.DriverAssignmentId);
                    table.ForeignKey(
                        name: "FK_DriverAssignments_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OdometerReadings",
                schema: "veh",
                columns: table => new
                {
                    OdometerReadingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Km = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdometerReadings", x => x.OdometerReadingId);
                    table.ForeignKey(
                        name: "FK_OdometerReadings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleAttachedItems",
                schema: "veh",
                columns: table => new
                {
                    VehicleAttachedItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    ItemTypeId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    InstallationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    WarrantyUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DetachedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DetachReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TransferredFromItemId = table.Column<int>(type: "int", nullable: true),
                    TransferredToVehicleId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleAttachedItems", x => x.VehicleAttachedItemId);
                    table.ForeignKey(
                        name: "FK_VehicleAttachedItems_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleLifecycle",
                schema: "veh",
                columns: table => new
                {
                    VehicleLifecycleEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    FromCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CounterpartyId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleLifecycle", x => x.VehicleLifecycleEntryId);
                    table.ForeignKey(
                        name: "FK_VehicleLifecycle_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleRelations",
                schema: "veh",
                columns: table => new
                {
                    VehicleRelationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CounterpartyId = table.Column<int>(type: "int", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    AgreementEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AgreementReference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SharePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    SharingBasis = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FixedMonthlyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ExpenseSharingRule = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    RentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RentFrequency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RentDueDay = table.Column<int>(type: "int", nullable: true),
                    SecurityDeposit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ArrangementType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AgreedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RevenueSharePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleRelations", x => x.VehicleRelationId);
                    table.ForeignKey(
                        name: "FK_VehicleRelations_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverAssignments_VehicleId",
                schema: "veh",
                table: "DriverAssignments",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "UX_DriverAssignments_Driver",
                schema: "veh",
                table: "DriverAssignments",
                columns: new[] { "TenantId", "DriverId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DriverAssignments_Vehicle",
                schema: "veh",
                table: "DriverAssignments",
                columns: new[] { "TenantId", "VehicleId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OdometerReadings_TenantId_VehicleId_ReadingDate",
                schema: "veh",
                table: "OdometerReadings",
                columns: new[] { "TenantId", "VehicleId", "ReadingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OdometerReadings_VehicleId",
                schema: "veh",
                table: "OdometerReadings",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleAttachedItems_TenantId_VehicleId_Status",
                schema: "veh",
                table: "VehicleAttachedItems",
                columns: new[] { "TenantId", "VehicleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleAttachedItems_VehicleId",
                schema: "veh",
                table: "VehicleAttachedItems",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "UX_VehicleItems_Serial",
                schema: "veh",
                table: "VehicleAttachedItems",
                columns: new[] { "TenantId", "SerialNo" },
                unique: true,
                filter: "[SerialNo] IS NOT NULL AND [Status] = 'Attached'");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLifecycle_TenantId_VehicleId_OccurredOn",
                schema: "veh",
                table: "VehicleLifecycle",
                columns: new[] { "TenantId", "VehicleId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLifecycle_VehicleId",
                schema: "veh",
                table: "VehicleLifecycle",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRelations_TenantId_CounterpartyId",
                schema: "veh",
                table: "VehicleRelations",
                columns: new[] { "TenantId", "CounterpartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRelations_VehicleId",
                schema: "veh",
                table: "VehicleRelations",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "UX_VehicleRelations_Open",
                schema: "veh",
                table: "VehicleRelations",
                columns: new[] { "TenantId", "VehicleId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_CurrentCategory",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "CurrentCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_DefaultDriverId",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "DefaultDriverId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_Status_ModifiedOn",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "Status", "ModifiedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_VehicleCode",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "VehicleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_VehicleTypeId",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "VehicleTypeId" });

            migrationBuilder.CreateIndex(
                name: "UX_Vehicles_Chassis",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "ChassisNo" },
                unique: true,
                filter: "[ChassisNo] IS NOT NULL AND [Status] <> 'Sold' AND [Status] <> 'Transferred' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Vehicles_Engine",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "EngineNo" },
                unique: true,
                filter: "[EngineNo] IS NOT NULL AND [Status] <> 'Sold' AND [Status] <> 'Transferred' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Vehicles_FuelCard",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "FuelCardNumber" },
                unique: true,
                filter: "[FuelCardNumber] IS NOT NULL AND [Status] IN ('Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable') AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Vehicles_RegNo",
                schema: "veh",
                table: "Vehicles",
                columns: new[] { "TenantId", "RegNoKey" },
                unique: true,
                filter: "[Status] <> 'Sold' AND [Status] <> 'Transferred' AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverAssignments",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "OdometerReadings",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "VehicleAttachedItems",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "VehicleLifecycle",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "VehicleRelations",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "Vehicles",
                schema: "veh");
        }
    }
}
