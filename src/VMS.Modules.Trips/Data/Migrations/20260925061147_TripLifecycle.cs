using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CompletionDate",
                schema: "trp",
                table: "Trips",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeldFromStatus",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldReason",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InactiveReason",
                schema: "trp",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelReason",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CompletionDate",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "HeldFromStatus",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "HoldReason",
                schema: "trp",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "InactiveReason",
                schema: "trp",
                table: "Trips");
        }
    }
}
