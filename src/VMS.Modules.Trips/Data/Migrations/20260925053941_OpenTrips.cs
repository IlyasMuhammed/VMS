using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class OpenTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FromCityId",
                schema: "trp",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromLocationType",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromOtherLocationName",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromOtherLocationType",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FromOtherNearestCityId",
                schema: "trp",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRoundTrip",
                schema: "trp",
                table: "Trips",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ToCityId",
                schema: "trp",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToLocationType",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToOtherLocationName",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToOtherLocationType",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToOtherNearestCityId",
                schema: "trp",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TripStops",
                schema: "trp",
                columns: table => new
                {
                    TripStopId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    LocationType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: true),
                    OtherLocationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OtherLocationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    OtherNearestCityId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStops", x => x.TripStopId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TenantId_TripId_Sequence",
                schema: "trp",
                table: "TripStops",
                columns: new[] { "TenantId", "TripId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripStops",
                schema: "trp");

            migrationBuilder.DropColumn(
                name: "FromCityId",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "FromLocationType",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "FromOtherLocationName",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "FromOtherLocationType",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "FromOtherNearestCityId",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "IsRoundTrip",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ToCityId",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ToLocationType",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ToOtherLocationName",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ToOtherLocationType",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ToOtherNearestCityId",
                schema: "trp",
                table: "Trips");
        }
    }
}
