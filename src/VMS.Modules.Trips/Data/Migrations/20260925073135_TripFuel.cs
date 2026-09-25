using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripFuel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripFuels",
                schema: "trp",
                columns: table => new
                {
                    TripFuelId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    FuelDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FuelType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(10,3)", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Odometer = table.Column<decimal>(type: "decimal(10,1)", nullable: true),
                    StationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CityId = table.Column<int>(type: "int", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FuelCardId = table.Column<int>(type: "int", nullable: true),
                    OtherPaymentText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttachmentId = table.Column<long>(type: "bigint", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<int>(type: "int", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripFuels", x => x.TripFuelId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripFuels_TenantId_TripId",
                schema: "trp",
                table: "TripFuels",
                columns: new[] { "TenantId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_TripFuels_TenantId_VehicleId_FuelDateTime",
                schema: "trp",
                table: "TripFuels",
                columns: new[] { "TenantId", "VehicleId", "FuelDateTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripFuels",
                schema: "trp");
        }
    }
}
