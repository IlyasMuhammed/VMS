using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerContactsAndBillingAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerBillingAddresses",
                schema: "trp",
                columns: table => new
                {
                    CustomerBillingAddressId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    AddressName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    ProvinceState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Ntn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Strn = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBillingAddresses", x => x.CustomerBillingAddressId);
                });

            migrationBuilder.CreateTable(
                name: "CustomerContacts",
                schema: "trp",
                columns: table => new
                {
                    CustomerContactId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Designation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Mobile1 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Mobile2 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Telephone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AvailabilityTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerContacts", x => x.CustomerContactId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillingAddresses_CustomerId",
                schema: "trp",
                table: "CustomerBillingAddresses",
                column: "CustomerId",
                unique: true,
                filter: "[IsDefault] = 1 AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillingAddresses_CustomerId_AddressName",
                schema: "trp",
                table: "CustomerBillingAddresses",
                columns: new[] { "CustomerId", "AddressName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBillingAddresses_TenantId_CustomerId",
                schema: "trp",
                table: "CustomerBillingAddresses",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_CustomerId",
                schema: "trp",
                table: "CustomerContacts",
                column: "CustomerId",
                unique: true,
                filter: "[IsPrimary] = 1 AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_TenantId_CustomerId",
                schema: "trp",
                table: "CustomerContacts",
                columns: new[] { "TenantId", "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerBillingAddresses",
                schema: "trp");

            migrationBuilder.DropTable(
                name: "CustomerContacts",
                schema: "trp");
        }
    }
}
