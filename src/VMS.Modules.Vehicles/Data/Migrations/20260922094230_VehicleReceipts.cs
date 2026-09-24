using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Vehicles.Data.Migrations
{
    /// <inheritdoc />
    public partial class VehicleReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentFileId",
                schema: "veh",
                table: "VehicleTransactions");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptContentType",
                schema: "veh",
                table: "VehicleTransactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptFileName",
                schema: "veh",
                table: "VehicleTransactions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptSha256",
                schema: "veh",
                table: "VehicleTransactions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReceiptSizeBytes",
                schema: "veh",
                table: "VehicleTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptStorageKey",
                schema: "veh",
                table: "VehicleTransactions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptContentType",
                schema: "veh",
                table: "VehicleTransactions");

            migrationBuilder.DropColumn(
                name: "ReceiptFileName",
                schema: "veh",
                table: "VehicleTransactions");

            migrationBuilder.DropColumn(
                name: "ReceiptSha256",
                schema: "veh",
                table: "VehicleTransactions");

            migrationBuilder.DropColumn(
                name: "ReceiptSizeBytes",
                schema: "veh",
                table: "VehicleTransactions");

            migrationBuilder.DropColumn(
                name: "ReceiptStorageKey",
                schema: "veh",
                table: "VehicleTransactions");

            migrationBuilder.AddColumn<Guid>(
                name: "AttachmentFileId",
                schema: "veh",
                table: "VehicleTransactions",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
