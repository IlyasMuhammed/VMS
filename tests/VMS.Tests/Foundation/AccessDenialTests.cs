using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-06: a refused request is answered with 403 and written to the denial log.</summary>
[Collection(ApiCollection.Name)]
public sealed class AccessDenialTests(ApiFactory factory)
{
    private HttpClient SignedInAs(string name, int userId, params string[] permissions) =>
        factory.CreateClient().WithToken(TestTokens.For(name, userId, permissions));

    private int DenialsFor(int userId) =>
        factory.Scalar<int>("SELECT COUNT(*) FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId));

    [Fact]
    public async Task A_user_without_the_permission_gets_403_and_the_denial_is_recorded()
    {
        const int userId = 9101;
        using var client = SignedInAs("Amina Khan", userId, PermissionCodes.USER_VIEW);

        var response = await client.PostAsJsonAsync("/api/users", new { firstName = "A", lastName = "B", email = "x@test.local", roleId = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, DenialsFor(userId));
        Assert.Equal(PermissionCodes.USER_MANAGE, factory.Scalar<string>("SELECT Permission FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId)));
        Assert.Equal("POST", factory.Scalar<string>("SELECT Method FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId)));
        Assert.Equal("/api/users", factory.Scalar<string>("SELECT Path FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId)));
        Assert.Equal("Amina Khan", factory.Scalar<string>("SELECT UserName FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId)));
        Assert.Equal(TenantDefaults.PlatformTenantId, factory.Scalar<Guid>("SELECT TenantId FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId)));
    }

    [Fact]
    public async Task The_record_keeps_which_item_was_targeted()
    {
        const int userId = 9102;
        using var client = SignedInAs("Bilal Ahmed", userId);

        var response = await client.PutAsJsonAsync("/api/users/42/role", new { roleId = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("id=42", factory.Scalar<string>("SELECT RouteValues FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId)));
    }

    [Fact]
    public async Task Repeated_denials_each_leave_a_row_so_they_can_be_reported()
    {
        const int userId = 9103;
        using var client = SignedInAs("Chen Wei", userId);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/roles")).StatusCode);

        Assert.Equal(3, DenialsFor(userId));
    }

    [Fact]
    public async Task A_request_that_is_allowed_is_not_recorded()
    {
        const int userId = 9104;
        using var client = SignedInAs("Dana Roy", userId, PermissionCodes.ROLE_VIEW);

        var response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, DenialsFor(userId));
    }

    [Fact]
    public async Task Not_signed_in_is_401_and_is_not_a_permission_denial()
    {
        using var client = factory.CreateClient();   // starting the host is what creates the database
        var before = factory.Scalar<int>("SELECT COUNT(*) FROM auth.AccessDenials");

        var response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, factory.Scalar<int>("SELECT COUNT(*) FROM auth.AccessDenials"));
    }

    [Fact]
    public async Task Refusing_a_super_admin_only_endpoint_names_that_as_the_missing_permission()
    {
        const int userId = 9105;
        using var client = SignedInAs("Eli Stone", userId, PermissionCodes.USER_MANAGE, PermissionCodes.ROLE_MANAGE);

        var response = await client.GetAsync("/api/system/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SUPER_ADMIN", factory.Scalar<string>("SELECT Permission FROM auth.AccessDenials WHERE UserId = @u", ("@u", userId)));
    }

    [Fact]
    public async Task A_failure_to_record_never_turns_a_403_into_a_500_or_lets_the_request_through()
    {
        using var broken = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddScoped<IAccessDenialSink, ThrowingSink>()));
        using var client = broken.CreateClient().WithToken(TestTokens.For("Fay Lin", 9106));

        var response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class ThrowingSink : IAccessDenialSink
    {
        public Task RecordAsync(AccessDenial denial) => throw new InvalidOperationException("The log is unavailable.");
    }
}
