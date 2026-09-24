using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-05: the permission catalogue and how it is seeded.</summary>
[Collection(ApiCollection.Name)]
public sealed class PermissionCatalogueTests(ApiFactory factory)
{
    private static readonly string[] ValidLevels = [PermissionLevels.Interface, PermissionLevels.Operation, PermissionLevels.Field];

    [Fact]
    public void Catalogue_holds_the_fifty_five_permissions_the_register_calls_for()
    {
        Assert.Equal(55, PermissionCodes.Catalog.Count);
    }

    [Fact]
    public void Codes_are_unique_and_every_entry_is_fully_described()
    {
        Assert.Equal(PermissionCodes.Catalog.Count, PermissionCodes.Catalog.Select(d => d.Code).Distinct().Count());
        Assert.All(PermissionCodes.Catalog, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.False(string.IsNullOrWhiteSpace(d.Module));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
            Assert.Contains(d.Level, ValidLevels);
        });
    }

    [Fact]
    public void A_permission_is_field_level_exactly_when_it_guards_a_value()
    {
        // The FSD names every field permission BP.FIELD.* or VEH.FIELD.*. Keeping the two in step means
        // the level column can be trusted by the role editor's Interface / Operation / Field grouping.
        foreach (var d in PermissionCodes.Catalog)
            Assert.Equal(d.Code.Contains(".FIELD.", StringComparison.Ordinal), d.Level == PermissionLevels.Field);
    }

    [Fact]
    public void The_fsd_duplicates_of_user_and_role_management_are_the_existing_codes()
    {
        Assert.DoesNotContain(PermissionCodes.Catalog, d => d.Code is "ADM.USER.MANAGE" or "ADM.ROLE.MANAGE");
        Assert.Contains(PermissionCodes.Catalog, d => d.Code == PermissionCodes.USER_MANAGE);
        Assert.Contains(PermissionCodes.Catalog, d => d.Code == PermissionCodes.ROLE_MANAGE);
    }

    [Fact]
    public void Every_permission_is_seeded_with_its_level()
    {
        factory.CreateClient();

        Assert.Equal(55, factory.Scalar<int>("SELECT COUNT(*) FROM auth.Permissions"));
        Assert.Equal(PermissionCodes.Catalog.Count(d => d.Level == PermissionLevels.Field),
            factory.Scalar<int>("SELECT COUNT(*) FROM auth.Permissions WHERE Level = 'Field'"));
        Assert.Equal("Field", factory.Scalar<string>("SELECT Level FROM auth.Permissions WHERE Code = 'VEH.FIELD.COST.VIEW'"));
        Assert.Equal("Interface", factory.Scalar<string>("SELECT Level FROM auth.Permissions WHERE Code = 'BP.VIEW'"));
    }

    [Fact]
    public void Administrator_roles_hold_everything_and_the_other_seeded_roles_keep_theirs()
    {
        factory.CreateClient();

        int Held(string roleCode) => factory.Scalar<int>(
            "SELECT COUNT(*) FROM auth.RolePermissions rp JOIN auth.Roles r ON r.RoleID = rp.RoleID WHERE r.IsGlobal = 1 AND r.RoleCode = @c",
            ("@c", roleCode));

        Assert.Equal(55, Held("SUPER_ADMIN"));
        Assert.Equal(55, Held("TENANT_ADMIN"));
        Assert.Equal(2, Held("MANAGER"));
        Assert.Equal(0, Held("STAFF"));
    }

    [Fact]
    public async Task A_permission_added_by_a_later_release_reaches_administrators_but_no_other_role()
    {
        factory.CreateClient();
        factory.Execute("INSERT INTO auth.Permissions (Name, Code, Module, Description, Level) VALUES ('Later','TEST.LATER','Test','Added by a later release','Operation')");
        try
        {
            // Starting the application again is what an upgrade does: it runs the seeder over existing data.
            using var restarted = factory.WithWebHostBuilder(_ => { });
            using var client = restarted.CreateClient();
            await client.GetAsync("/health");

            int Holds(string roleCode) => factory.Scalar<int>(
                @"SELECT COUNT(*) FROM auth.RolePermissions rp
                  JOIN auth.Roles r ON r.RoleID = rp.RoleID
                  JOIN auth.Permissions p ON p.PermissionID = rp.PermissionID
                  WHERE r.IsGlobal = 1 AND r.RoleCode = @c AND p.Code = 'TEST.LATER'", ("@c", roleCode));

            Assert.Equal(1, Holds("TENANT_ADMIN"));
            Assert.Equal(1, Holds("SUPER_ADMIN"));
            Assert.Equal(0, Holds("MANAGER"));
            Assert.Equal(0, Holds("STAFF"));
        }
        finally
        {
            factory.Execute("DELETE FROM auth.Permissions WHERE Code = 'TEST.LATER'");
        }
    }
}
