using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class FuelCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuelCardAssignments",
                schema: "trp",
                columns: table => new
                {
                    FuelCardAssignmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FuelCardId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    DriverId = table.Column<int>(type: "int", nullable: true),
                    AssignedFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    AssignedTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelCardAssignments", x => x.FuelCardAssignmentId);
                });

            migrationBuilder.CreateTable(
                name: "FuelCards",
                schema: "trp",
                columns: table => new
                {
                    FuelCardId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FuelCardCompanyId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    DriverId = table.Column<int>(type: "int", nullable: true),
                    CardHolderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthlyLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelCards", x => x.FuelCardId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelCardAssignments_FuelCardId",
                schema: "trp",
                table: "FuelCardAssignments",
                column: "FuelCardId",
                unique: true,
                filter: "[AssignedTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FuelCardAssignments_TenantId_FuelCardId",
                schema: "trp",
                table: "FuelCardAssignments",
                columns: new[] { "TenantId", "FuelCardId" });

            migrationBuilder.CreateIndex(
                name: "IX_FuelCards_TenantId_CardNumber",
                schema: "trp",
                table: "FuelCards",
                columns: new[] { "TenantId", "CardNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelCards_TenantId_FuelCardCompanyId",
                schema: "trp",
                table: "FuelCards",
                columns: new[] { "TenantId", "FuelCardCompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuelCardAssignments",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "FuelCards",
                schema: "trp");
        }
    }
}
