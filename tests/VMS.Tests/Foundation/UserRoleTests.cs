using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-04: a user holds roles through UserRole, each with its own scope.</summary>
[Collection(ApiCollection.Name)]
public sealed class UserRoleTests(ApiFactory factory)
{
    private const string Password = "Passw0rd!Test";

    [Fact]
    public void The_seeded_super_admin_holds_their_role_everywhere()
    {
        factory.CreateClient();

        var rows = factory.Scalar<int>(
            @"SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.UserAccounts u ON u.UserID = ur.UserID
              WHERE u.Email = @e AND ur.ScopeType = 'AllBranches' AND ur.BranchId IS NULL AND ur.RoleID = u.RoleID",
            ("@e", ApiFactory.AdminEmail));

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task A_new_user_gets_their_role_row_when_created()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var email = $"newuser.{Guid.NewGuid():N}@test.local";
        var userId = await admin.CreateActiveUserAsync(email, Password, "MANAGER");
        var managerRoleId = await admin.RoleIdAsync("MANAGER");

        var rows = factory.Scalar<int>(
            "SELECT COUNT(*) FROM auth.UserRoles WHERE UserID = @u AND RoleID = @r AND ScopeType = 'AllBranches'",
            ("@u", userId), ("@r", managerRoleId));

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Changing_a_users_role_replaces_the_row_and_keeps_the_scope()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync($"swap.{Guid.NewGuid():N}@test.local", Password, "STAFF");
        var branch = Guid.NewGuid();
        factory.Execute("UPDATE auth.UserRoles SET ScopeType = 'OwnBranch', BranchId = @b WHERE UserID = @u", ("@b", branch), ("@u", userId));
        var managerRoleId = await admin.RoleIdAsync("MANAGER");

        var response = await admin.PutAsJsonAsync($"/api/users/{userId}/role", new { roleId = managerRoleId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM auth.UserRoles WHERE UserID = @u", ("@u", userId)));
        Assert.Equal(managerRoleId, factory.Scalar<int>("SELECT RoleID FROM auth.UserRoles WHERE UserID = @u", ("@u", userId)));
        Assert.Equal("OwnBranch", factory.Scalar<string>("SELECT ScopeType FROM auth.UserRoles WHERE UserID = @u", ("@u", userId)));
        Assert.Equal(branch, factory.Scalar<Guid>("SELECT BranchId FROM auth.UserRoles WHERE UserID = @u", ("@u", userId)));
        // The primary role on the account moved with it, so the two never disagree.
        Assert.Equal(managerRoleId, factory.Scalar<int>("SELECT RoleID FROM auth.UserAccounts WHERE UserID = @u", ("@u", userId)));
    }

    [Fact]
    public async Task A_user_cannot_hold_the_same_role_twice()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync($"dup.{Guid.NewGuid():N}@test.local", Password);
        var roleId = factory.Scalar<int>("SELECT RoleID FROM auth.UserRoles WHERE UserID = @u", ("@u", userId));

        var ex = Assert.Throws<SqlException>(() => factory.Execute(
            "INSERT INTO auth.UserRoles (UserID, RoleID, ScopeType, TenantId) SELECT UserID, RoleID, 'AllBranches', TenantId FROM auth.UserRoles WHERE UserID = @u",
            ("@u", userId)));

        Assert.Contains("IX_UserRoles_UserID_RoleID", ex.Message);
        Assert.True(roleId > 0);
    }

    [Fact]
    public void Every_user_has_at_least_one_role_row_matching_their_primary_role()
    {
        factory.CreateClient();

        var withoutRow = factory.Scalar<int>(
            @"SELECT COUNT(*) FROM auth.UserAccounts u WHERE u.IsDeleted = 0
              AND NOT EXISTS (SELECT 1 FROM auth.UserRoles ur WHERE ur.UserID = u.UserID AND ur.RoleID = u.RoleID)");

        Assert.Equal(0, withoutRow);
    }
}
