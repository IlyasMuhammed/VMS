using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.BusinessPartners.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bp");

            migrationBuilder.CreateTable(
                name: "BpRoleLog",
                schema: "bp",
                columns: table => new
                {
                    BpRoleLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    RoleCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpRoleLog", x => x.BpRoleLogId);
                });

            migrationBuilder.CreateTable(
                name: "BpStatusLog",
                schema: "bp",
                columns: table => new
                {
                    BpStatusLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpStatusLog", x => x.BpStatusLogId);
                });

            migrationBuilder.CreateTable(
                name: "BusinessPartners",
                schema: "bp",
                columns: table => new
                {
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BpCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PartyType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Cnic = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Ntn = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Strn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FilerStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PrimaryMobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AlternatePhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OpeningBalanceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPartners", x => x.BusinessPartnerId);
                });

            migrationBuilder.CreateTable(
                name: "BpAddresses",
                schema: "bp",
                columns: table => new
                {
                    BpAddressId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    AddressType = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Line1 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Line2 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    ProvinceCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Landmark = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpAddresses", x => x.BpAddressId);
                    table.ForeignKey(
                        name: "FK_BpAddresses_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "bp",
                        principalTable: "BusinessPartners",
                        principalColumn: "BusinessPartnerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BpBankAccounts",
                schema: "bp",
                columns: table => new
                {
                    BpBankAccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    AccountTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpBankAccounts", x => x.BpBankAccountId);
                    table.ForeignKey(
                        name: "FK_BpBankAccounts_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "bp",
                        principalTable: "BusinessPartners",
                        principalColumn: "BusinessPartnerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BpContacts",
                schema: "bp",
                columns: table => new
                {
                    BpContactId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Designation = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Mobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpContacts", x => x.BpContactId);
                    table.ForeignKey(
                        name: "FK_BpContacts_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "bp",
                        principalTable: "BusinessPartners",
                        principalColumn: "BusinessPartnerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BpCustomerDetails",
                schema: "bp",
                columns: table => new
                {
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerType = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    RateBasis = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    DefaultRate = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreditDays = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpCustomerDetails", x => x.BusinessPartnerId);
                    table.ForeignKey(
                        name: "FK_BpCustomerDetails_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "bp",
                        principalTable: "BusinessPartners",
                        principalColumn: "BusinessPartnerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BpDriverDetails",
                schema: "bp",
                columns: table => new
                {
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenceNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LicenceType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    LicenceIssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LicenceExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EmploymentType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DateOfJoining = table.Column<DateOnly>(type: "date", nullable: true),
                    MonthlyRate = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CommissionBasis = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CommissionValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BloodGroup = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    EmergencyContactName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmergencyContactPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Guarantor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DriverAppAccess = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpDriverDetails", x => x.BusinessPartnerId);
                    table.ForeignKey(
                        name: "FK_BpDriverDetails_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "bp",
                        principalTable: "BusinessPartners",
                        principalColumn: "BusinessPartnerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BpVendorDetails",
                schema: "bp",
                columns: table => new
                {
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplyCategories = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PaymentTermDays = table.Column<int>(type: "int", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpVendorDetails", x => x.BusinessPartnerId);
                    table.ForeignKey(
                        name: "FK_BpVendorDetails_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "bp",
                        principalTable: "BusinessPartners",
                        principalColumn: "BusinessPartnerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessPartnerRoles",
                schema: "bp",
                columns: table => new
                {
                    BpRoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    RoleCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPartnerRoles", x => x.BpRoleId);
                    table.ForeignKey(
                        name: "FK_BusinessPartnerRoles_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "bp",
                        principalTable: "BusinessPartners",
                        principalColumn: "BusinessPartnerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BpAddresses_TenantId",
                schema: "bp",
                table: "BpAddresses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_BpAddresses_Primary",
                schema: "bp",
                table: "BpAddresses",
                column: "BusinessPartnerId",
                unique: true,
                filter: "[IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BpBankAccounts_BusinessPartnerId_BankName_AccountNumber",
                schema: "bp",
                table: "BpBankAccounts",
                columns: new[] { "BusinessPartnerId", "BankName", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BpBankAccounts_TenantId",
                schema: "bp",
                table: "BpBankAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_BpBankAccounts_Primary",
                schema: "bp",
                table: "BpBankAccounts",
                column: "BusinessPartnerId",
                unique: true,
                filter: "[IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BpContacts_TenantId",
                schema: "bp",
                table: "BpContacts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_BpContacts_Primary",
                schema: "bp",
                table: "BpContacts",
                column: "BusinessPartnerId",
                unique: true,
                filter: "[IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BpCustomerDetails_TenantId",
                schema: "bp",
                table: "BpCustomerDetails",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BpDriverDetails_TenantId_LicenceNo",
                schema: "bp",
                table: "BpDriverDetails",
                columns: new[] { "TenantId", "LicenceNo" });

            migrationBuilder.CreateIndex(
                name: "IX_BpRoleLog_BusinessPartnerId_OccurredOn",
                schema: "bp",
                table: "BpRoleLog",
                columns: new[] { "BusinessPartnerId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_BpRoleLog_TenantId",
                schema: "bp",
                table: "BpRoleLog",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BpStatusLog_BusinessPartnerId_OccurredOn",
                schema: "bp",
                table: "BpStatusLog",
                columns: new[] { "BusinessPartnerId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_BpStatusLog_TenantId",
                schema: "bp",
                table: "BpStatusLog",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BpVendorDetails_TenantId",
                schema: "bp",
                table: "BpVendorDetails",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartnerRoles_BusinessPartnerId_RoleCode",
                schema: "bp",
                table: "BusinessPartnerRoles",
                columns: new[] { "BusinessPartnerId", "RoleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartnerRoles_TenantId_RoleCode_IsActive",
                schema: "bp",
                table: "BusinessPartnerRoles",
                columns: new[] { "TenantId", "RoleCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartners_TenantId_BpCode",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "BpCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartners_TenantId_CityId",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "CityId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartners_TenantId_LegalName",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "LegalName" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartners_TenantId_PrimaryMobile",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "PrimaryMobile" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartners_TenantId_Status_ModifiedOn",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "Status", "ModifiedOn" });

            migrationBuilder.CreateIndex(
                name: "UX_BusinessPartners_Cnic",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "Cnic" },
                unique: true,
                filter: "[Cnic] IS NOT NULL AND [Status] <> 'Merged' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessPartners_Ntn",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "Ntn" },
                unique: true,
                filter: "[Ntn] IS NOT NULL AND [PartyType] = 'Company' AND [Status] <> 'Merged' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessPartners_Strn",
                schema: "bp",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "Strn" },
                unique: true,
                filter: "[Strn] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BpAddresses",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BpBankAccounts",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BpContacts",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BpCustomerDetails",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BpDriverDetails",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BpRoleLog",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BpStatusLog",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BpVendorDetails",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BusinessPartnerRoles",
                schema: "bp");

            migrationBuilder.DropTable(
                name: "BusinessPartners",
                schema: "bp");
        }
    }
}
