using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class NumberSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NumberSeries",
                schema: "core",
                columns: table => new
                {
                    NumberSeriesID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Padding = table.Column<int>(type: "int", nullable: false),
                    ResetPeriod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeries", x => x.NumberSeriesID);
                });

            migrationBuilder.CreateTable(
                name: "NumberSeriesCounters",
                schema: "core",
                columns: table => new
                {
                    NumberSeriesID = table.Column<int>(type: "int", nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeriesCounters", x => new { x.NumberSeriesID, x.PeriodKey });
                    table.ForeignKey(
                        name: "FK_NumberSeriesCounters_NumberSeries_NumberSeriesID",
                        column: x => x.NumberSeriesID,
                        principalSchema: "core",
                        principalTable: "NumberSeries",
                        principalColumn: "NumberSeriesID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NumberSeries_TenantId_Code",
                schema: "core",
                table: "NumberSeries",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NumberSeriesCounters",
                schema: "core");

            migrationBuilder.DropTable(
                name: "NumberSeries",
                schema: "core");
        }
    }
}
