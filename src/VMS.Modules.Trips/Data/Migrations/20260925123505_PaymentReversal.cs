using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                schema: "trp",
                table: "InvoicePayments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReversedBy",
                schema: "trp",
                table: "InvoicePayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedOn",
                schema: "trp",
                table: "InvoicePayments",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReversalReason",
                schema: "trp",
                table: "InvoicePayments");

            migrationBuilder.DropColumn(
                name: "ReversedBy",
                schema: "trp",
                table: "InvoicePayments");

            migrationBuilder.DropColumn(
                name: "ReversedOn",
                schema: "trp",
                table: "InvoicePayments");
        }
    }
}
