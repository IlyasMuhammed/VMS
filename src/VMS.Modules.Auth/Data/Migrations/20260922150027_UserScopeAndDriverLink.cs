using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VMS.Modules.Auth.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserScopeAndDriverLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinkedPartnerId",
                schema: "auth",
                table: "UserAccounts",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkedPartnerId",
                schema: "auth",
                table: "UserAccounts");
        }
    }
}
