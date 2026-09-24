using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Vehicles.Data.Migrations
{
    /// <inheritdoc />
    public partial class VehicleFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleAcquisitions",
                schema: "veh",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AmountPaid = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PaymentMode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    RegistrationCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleAcquisitions", x => x.VehicleId);
                    table.ForeignKey(
                        name: "FK_VehicleAcquisitions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleFinanceAgreements",
                schema: "veh",
                columns: table => new
                {
                    VehicleFinanceAgreementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    FinanceTypeId = table.Column<int>(type: "int", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    AgreementNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AgreementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FinanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DownPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InstallmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Tenure = table.Column<int>(type: "int", nullable: false),
                    FirstDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MarkupRate = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    ResidualAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    SecurityDeposit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleFinanceAgreements", x => x.VehicleFinanceAgreementId);
                    table.ForeignKey(
                        name: "FK_VehicleFinanceAgreements_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleInstallments",
                schema: "veh",
                columns: table => new
                {
                    VehicleInstallmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    VehicleFinanceAgreementId = table.Column<int>(type: "int", nullable: false),
                    InstallmentNo = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsResidual = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleInstallments", x => x.VehicleInstallmentId);
                    table.ForeignKey(
                        name: "FK_VehicleInstallments_VehicleFinanceAgreements_VehicleFinanceAgreementId",
                        column: x => x.VehicleFinanceAgreementId,
                        principalSchema: "veh",
                        principalTable: "VehicleFinanceAgreements",
                        principalColumn: "VehicleFinanceAgreementId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTransactions",
                schema: "veh",
                columns: table => new
                {
                    VehicleTransactionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsSystemGenerated = table.Column<bool>(type: "bit", nullable: false),
                    AttachmentFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversesTransactionId = table.Column<int>(type: "int", nullable: true),
                    InstallmentId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTransactions", x => x.VehicleTransactionId);
                    table.ForeignKey(
                        name: "FK_VehicleTransactions_VehicleInstallments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalSchema: "veh",
                        principalTable: "VehicleInstallments",
                        principalColumn: "VehicleInstallmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleTransactions_VehicleTransactions_ReversesTransactionId",
                        column: x => x.ReversesTransactionId,
                        principalSchema: "veh",
                        principalTable: "VehicleTransactions",
                        principalColumn: "VehicleTransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleTransactions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "veh",
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleAcquisitions_TenantId",
                schema: "veh",
                table: "VehicleAcquisitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleFinanceAgreements_VehicleId",
                schema: "veh",
                table: "VehicleFinanceAgreements",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "UX_VehicleFinance_BankAgreement",
                schema: "veh",
                table: "VehicleFinanceAgreements",
                columns: new[] { "TenantId", "BankId", "AgreementNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_VehicleFinance_Open",
                schema: "veh",
                table: "VehicleFinanceAgreements",
                columns: new[] { "TenantId", "VehicleId" },
                unique: true,
                filter: "[Status] IN ('Draft', 'Active')");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleInstallments_TenantId_VehicleId_DueDate",
                schema: "veh",
                table: "VehicleInstallments",
                columns: new[] { "TenantId", "VehicleId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleInstallments_VehicleFinanceAgreementId_InstallmentNo",
                schema: "veh",
                table: "VehicleInstallments",
                columns: new[] { "VehicleFinanceAgreementId", "InstallmentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTransactions_InstallmentId",
                schema: "veh",
                table: "VehicleTransactions",
                column: "InstallmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTransactions_TenantId_VehicleId_TransactionDate",
                schema: "veh",
                table: "VehicleTransactions",
                columns: new[] { "TenantId", "VehicleId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTransactions_TenantId_VehicleId_Type",
                schema: "veh",
                table: "VehicleTransactions",
                columns: new[] { "TenantId", "VehicleId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTransactions_VehicleId",
                schema: "veh",
                table: "VehicleTransactions",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "UX_VehicleTransactions_Reversal",
                schema: "veh",
                table: "VehicleTransactions",
                column: "ReversesTransactionId",
                unique: true,
                filter: "[ReversesTransactionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleAcquisitions",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "VehicleTransactions",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "VehicleInstallments",
                schema: "veh");

            migrationBuilder.DropTable(
                name: "VehicleFinanceAgreements",
                schema: "veh");
        }
    }
}
