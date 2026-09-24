using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using VMS.API.Middleware;
using VMS.Shared.Exceptions;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-01 and S0-FND-02: the platform boots, migrates, seeds and reports errors consistently.</summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_needs_no_token()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_callers()
    {
        var response = await factory.CreateClient().GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Start_up_migrates_every_module_and_seeds_the_reference_data()
    {
        factory.CreateClient(); // make sure the host has started

        Assert.True(factory.Scalar<int>("SELECT COUNT(*) FROM tenancy.__EFMigrationsHistory") >= 1);
        Assert.True(factory.Scalar<int>("SELECT COUNT(*) FROM auth.__EFMigrationsHistory") >= 1);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM tenancy.Tenants WHERE TenantCode = 'VMS-PLATFORM'"));
        // SuperAdmin, TenantAdmin, Manager, Staff, and §23B.7's five default role templates.
        Assert.Equal(9, factory.Scalar<int>("SELECT COUNT(*) FROM auth.Roles WHERE IsGlobal = 1"));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM auth.UserAccounts WHERE Email = @e", ("@e", ApiFactory.AdminEmail)));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM tenancy.SuperAdminUsers"));
    }

    [Fact]
    public async Task Start_up_is_repeatable_without_duplicating_seed_data()
    {
        // A second host over the same, already-seeded database must not create a second Super Admin or role set.
        var before = factory.Scalar<int>("SELECT COUNT(*) FROM auth.Roles");
        using var second = factory.WithWebHostBuilder(_ => { });
        using var client = second.CreateClient();
        await client.GetAsync("/health");

        Assert.Equal(before, factory.Scalar<int>("SELECT COUNT(*) FROM auth.Roles"));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM tenancy.SuperAdminUsers"));
    }

    [Theory]
    [InlineData(typeof(BadRequestException), 400)]
    [InlineData(typeof(UnauthorizedException), 401)]
    [InlineData(typeof(ForbiddenException), 403)]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(ConflictException), 409)]
    public async Task Domain_exceptions_become_their_http_status_with_the_message(Type exceptionType, int expectedStatus)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(_ => throw (Exception)Activator.CreateInstance(exceptionType, "the reason")!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = JsonDocument.Parse(context.Response.Body);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("the reason", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unexpected_errors_become_a_500_that_leaks_no_detail()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(_ => throw new InvalidOperationException("secret connection string"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("secret", text);
    }
}
