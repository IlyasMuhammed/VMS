using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerBillingConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerBillingConfigurations",
                schema: "trp",
                columns: table => new
                {
                    CustomerBillingConfigurationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DefaultBillingAddressId = table.Column<long>(type: "bigint", nullable: true),
                    DefaultInvoiceTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    InvoiceNumberPrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PodRequired = table.Column<bool>(type: "bit", nullable: false),
                    EvidenceRequired = table.Column<bool>(type: "bit", nullable: false),
                    EvidencePageSize = table.Column<int>(type: "int", nullable: false),
                    DuplicateReferenceBehaviour = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CustomerReferenceRequired = table.Column<bool>(type: "bit", nullable: false),
                    StatementEmailContactId = table.Column<long>(type: "bigint", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBillingConfigurations", x => x.CustomerBillingConfigurationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillingConfigurations_CustomerId",
                schema: "trp",
                table: "CustomerBillingConfigurations",
                column: "CustomerId",
                unique: true,
                filter: "[EffectiveTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillingConfigurations_TenantId_CustomerId",
                schema: "trp",
                table: "CustomerBillingConfigurations",
                columns: new[] { "TenantId", "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerBillingConfigurations",
                schema: "trp");
        }
    }
}
