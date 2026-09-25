using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class Trips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripNumberCounters",
                schema: "trp",
                columns: table => new
                {
                    TripNumberCounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripNumberCounters", x => x.TripNumberCounterId);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                schema: "trp",
                columns: table => new
                {
                    TripId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TripType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CustomerTripReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TripConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    RouteId = table.Column<int>(type: "int", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    IsDriverOverridden = table.Column<bool>(type: "bit", nullable: false),
                    DriverOverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TripDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartOdometer = table.Column<decimal>(type: "decimal(10,1)", nullable: true),
                    EndOdometer = table.Column<decimal>(type: "decimal(10,1)", nullable: true),
                    TripRateId = table.Column<long>(type: "bigint", nullable: true),
                    TripRateAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RateEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    RateEffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RateSource = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    TripAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    RateMissing = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.TripId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripNumberCounters_TenantId_Year",
                schema: "trp",
                table: "TripNumberCounters",
                columns: new[] { "TenantId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TenantId_CustomerId",
                schema: "trp",
                table: "Trips",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TenantId_TripConfigurationId",
                schema: "trp",
                table: "Trips",
                columns: new[] { "TenantId", "TripConfigurationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TenantId_TripNumber",
                schema: "trp",
                table: "Trips",
                columns: new[] { "TenantId", "TripNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TenantId_VehicleId",
                schema: "trp",
                table: "Trips",
                columns: new[] { "TenantId", "VehicleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripNumberCounters",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "Trips",
                schema: "trp");
        }
    }
}
