using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class DriverAppReviewQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEventId",
                schema: "trp",
                table: "TripIssues",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEventId",
                schema: "trp",
                table: "TripFuels",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEventId",
                schema: "trp",
                table: "TripExpenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TenantId_DriverId_Status",
                schema: "trp",
                table: "Trips",
                columns: new[] { "TenantId", "DriverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TenantId_Status_Source",
                schema: "trp",
                table: "Trips",
                columns: new[] { "TenantId", "Status", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_TripIssues_TripId_ClientEventId",
                schema: "trp",
                table: "TripIssues",
                columns: new[] { "TripId", "ClientEventId" },
                unique: true,
                filter: "[ClientEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TripFuels_TripId_ClientEventId",
                schema: "trp",
                table: "TripFuels",
                columns: new[] { "TripId", "ClientEventId" },
                unique: true,
                filter: "[ClientEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TripExpenses_TripId_ClientEventId",
                schema: "trp",
                table: "TripExpenses",
                columns: new[] { "TripId", "ClientEventId" },
                unique: true,
                filter: "[ClientEventId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_TenantId_DriverId_Status",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_TenantId_Status_Source",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_TripIssues_TripId_ClientEventId",
                schema: "trp",
                table: "TripIssues");

            migrationBuilder.DropIndex(
                name: "IX_TripFuels_TripId_ClientEventId",
                schema: "trp",
                table: "TripFuels");

            migrationBuilder.DropIndex(
                name: "IX_TripExpenses_TripId_ClientEventId",
                schema: "trp",
                table: "TripExpenses");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ClientEventId",
                schema: "trp",
                table: "TripIssues");

            migrationBuilder.DropColumn(
                name: "ClientEventId",
                schema: "trp",
                table: "TripFuels");

            migrationBuilder.DropColumn(
                name: "ClientEventId",
                schema: "trp",
                table: "TripExpenses");
        }
    }
}
