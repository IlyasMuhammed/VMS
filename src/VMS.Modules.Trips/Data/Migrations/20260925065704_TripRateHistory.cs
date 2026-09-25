using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripRateHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripRateHistories",
                schema: "trp",
                columns: table => new
                {
                    TripRateHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    OldTripRateId = table.Column<long>(type: "bigint", nullable: true),
                    OldRateAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    OldRateSource = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    OldCurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    NewTripRateId = table.Column<long>(type: "bigint", nullable: true),
                    NewRateAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NewRateSource = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    NewCurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PerformedBy = table.Column<int>(type: "int", nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripRateHistories", x => x.TripRateHistoryId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripRateHistories_TenantId_TripId",
                schema: "trp",
                table: "TripRateHistories",
                columns: new[] { "TenantId", "TripId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripRateHistories",
                schema: "trp");
        }
    }
}
