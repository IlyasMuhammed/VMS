using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-06: default deny. An endpoint that does not say how it is protected stops the app starting.</summary>
[Collection(ApiCollection.Name)]
public sealed class EndpointCoverageTests(ApiFactory factory)
{
    [Fact]
    public void The_real_api_starts_which_means_every_one_of_its_endpoints_declares_its_protection()
    {
        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void An_action_that_forgot_to_declare_its_protection_is_refused_at_start_up()
    {
        using var host = factory.With<UnprotectedProbeController>().Build();

        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        var message = Flatten(ex);
        Assert.Contains("must declare how it is protected", message);
        Assert.Contains("UnprotectedProbe.Get", message);
    }

    [Theory]
    [InlineData(typeof(PermissionProbe), true)]
    [InlineData(typeof(SuperAdminProbe), true)]
    [InlineData(typeof(SignedInProbe), true)]
    [InlineData(typeof(AnonymousProbe), true)]
    [InlineData(typeof(BareAuthorizeProbe), false)]   // "any signed-in user" must be said on purpose, not by accident
    [InlineData(typeof(NamedOtherPolicyProbe), false)]
    [InlineData(typeof(NothingProbe), false)]
    public void Only_an_explicit_statement_counts(Type controller, bool expected)
    {
        var metadata = controller.GetCustomAttributes(inherit: true).Cast<object>();

        Assert.Equal(expected, EndpointAuthorizationCoverage.IsExplicit(metadata));
    }

    [RequirePermission(PermissionCodes.USER_VIEW)] private sealed class PermissionProbe;
    [RequireSuperAdmin] private sealed class SuperAdminProbe;
    [AuthenticatedOnly] private sealed class SignedInProbe;
    [AllowAnonymous] private sealed class AnonymousProbe;
    [Authorize] private sealed class BareAuthorizeProbe;
    [Authorize(Policy = "Something")] private sealed class NamedOtherPolicyProbe;
    private sealed class NothingProbe;

    private static string Flatten(Exception ex) =>
        string.Join(Environment.NewLine, Chain(ex).Select(e => e.Message));

    private static IEnumerable<Exception> Chain(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException) yield return ex;
    }
}
