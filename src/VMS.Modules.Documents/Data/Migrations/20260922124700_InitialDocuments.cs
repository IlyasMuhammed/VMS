using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Documents.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "doc");

            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "doc",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StorageKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinkedTransactionId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.DocumentId);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                schema: "doc",
                columns: table => new
                {
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AppliesTo = table.Column<int>(type: "int", nullable: false),
                    PartnerRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsExpirable = table.Column<bool>(type: "bit", nullable: false),
                    DefaultValidityValue = table.Column<int>(type: "int", nullable: true),
                    DefaultValidityUnit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IsPeriodic = table.Column<bool>(type: "bit", nullable: false),
                    RenewalLeadDays = table.Column<int>(type: "int", nullable: false),
                    MandatoryLevel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RequiresDocumentNumber = table.Column<bool>(type: "bit", nullable: false),
                    HasCost = table.Column<bool>(type: "bit", nullable: false),
                    AllowedFormats = table.Column<int>(type: "int", nullable: false),
                    MaxFileSizeMb = table.Column<int>(type: "int", nullable: false),
                    RetentionYears = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.DocumentTypeId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_DocumentTypeId",
                schema: "doc",
                table: "Documents",
                columns: new[] { "TenantId", "DocumentTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_Status_ExpiryDate",
                schema: "doc",
                table: "Documents",
                columns: new[] { "TenantId", "Status", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "UX_Documents_Current",
                schema: "doc",
                table: "Documents",
                columns: new[] { "TenantId", "OwnerType", "OwnerId", "DocumentTypeId" },
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Documents_Version",
                schema: "doc",
                table: "Documents",
                columns: new[] { "TenantId", "OwnerType", "OwnerId", "DocumentTypeId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_TenantId_AppliesTo",
                schema: "doc",
                table: "DocumentTypes",
                columns: new[] { "TenantId", "AppliesTo" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_TenantId_Code",
                schema: "doc",
                table: "DocumentTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documents",
                schema: "doc");

            migrationBuilder.DropTable(
                name: "DocumentTypes",
                schema: "doc");
        }
    }
}
