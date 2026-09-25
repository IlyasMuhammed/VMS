using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripConfigurations",
                schema: "trp",
                columns: table => new
                {
                    TripConfigurationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    TripCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    DirectionType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripConfigurations", x => x.TripConfigurationId);
                });

            migrationBuilder.CreateTable(
                name: "TripConfigurationStops",
                schema: "trp",
                columns: table => new
                {
                    TripConfigurationStopId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: true),
                    OtherLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StopType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripConfigurationStops", x => x.TripConfigurationStopId);
                });

            migrationBuilder.CreateTable(
                name: "TripConfigurationVehicles",
                schema: "trp",
                columns: table => new
                {
                    TripConfigurationVehicleId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripConfigurationVehicles", x => x.TripConfigurationVehicleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripConfigurations_TenantId_CustomerId",
                schema: "trp",
                table: "TripConfigurations",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TripConfigurations_TenantId_CustomerId_TripCode",
                schema: "trp",
                table: "TripConfigurations",
                columns: new[] { "TenantId", "CustomerId", "TripCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripConfigurationStops_TenantId_TripConfigurationId_Sequence",
                schema: "trp",
                table: "TripConfigurationStops",
                columns: new[] { "TenantId", "TripConfigurationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripConfigurationVehicles_TenantId_TripConfigurationId",
                schema: "trp",
                table: "TripConfigurationVehicles",
                columns: new[] { "TenantId", "TripConfigurationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TripConfigurationVehicles_TenantId_VehicleId",
                schema: "trp",
                table: "TripConfigurationVehicles",
                columns: new[] { "TenantId", "VehicleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripConfigurations",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "TripConfigurationStops",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "TripConfigurationVehicles",
                schema: "trp");
        }
    }
}
