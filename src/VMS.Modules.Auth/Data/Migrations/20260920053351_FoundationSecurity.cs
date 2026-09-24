using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Auth.Data.Migrations
{
    /// <inheritdoc />
    public partial class FoundationSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Level",
                schema: "auth",
                table: "Permissions",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Operation");

            migrationBuilder.CreateTable(
                name: "AccessDenials",
                schema: "auth",
                columns: table => new
                {
                    AccessDenialID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Permission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RouteValues = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessDenials", x => x.AccessDenialID);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "auth",
                columns: table => new
                {
                    UserRoleID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    RoleID = table.Column<int>(type: "int", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "AllBranches"),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.UserRoleID);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleID",
                        column: x => x.RoleID,
                        principalSchema: "auth",
                        principalTable: "Roles",
                        principalColumn: "RoleID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_UserAccounts_UserID",
                        column: x => x.UserID,
                        principalSchema: "auth",
                        principalTable: "UserAccounts",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessDenials_TenantId_Permission_OccurredAt",
                schema: "auth",
                table: "AccessDenials",
                columns: new[] { "TenantId", "Permission", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessDenials_TenantId_UserId_OccurredAt",
                schema: "auth",
                table: "AccessDenials",
                columns: new[] { "TenantId", "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleID",
                schema: "auth",
                table: "UserRoles",
                column: "RoleID");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_TenantId_RoleID",
                schema: "auth",
                table: "UserRoles",
                columns: new[] { "TenantId", "RoleID" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserID_RoleID",
                schema: "auth",
                table: "UserRoles",
                columns: new[] { "UserID", "RoleID" },
                unique: true);

            // Every existing user holds exactly the role they already had, everywhere. New users get
            // their row when they are created (see UserService, TenantUserProvisioningService, AuthDataSeeder).
            migrationBuilder.Sql(
                @"INSERT INTO [auth].[UserRoles] ([UserID], [RoleID], [ScopeType], [TenantId])
                  SELECT [UserID], [RoleID], N'AllBranches', [TenantId]
                  FROM [auth].[UserAccounts]
                  WHERE [IsDeleted] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessDenials",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "auth");

            migrationBuilder.DropColumn(
                name: "Level",
                schema: "auth",
                table: "Permissions");
        }
    }
}
