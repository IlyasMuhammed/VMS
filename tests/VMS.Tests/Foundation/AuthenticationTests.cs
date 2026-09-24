using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-03: sign in, password hashing, token issue, refresh rotation, sign out and lockout.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthenticationTests(ApiFactory factory)
{
    private const string Password = "Passw0rd!Test";

    [Fact]
    public async Task Sign_in_returns_a_token_pair_and_the_users_permissions()
    {
        var response = await factory.CreateClient().TryLoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.DataAsync();
        Assert.Equal(1800, data.GetProperty("expiresIn").GetInt32());
        Assert.True(data.GetProperty("user").GetProperty("isSuperAdmin").GetBoolean());

        var token = new JwtSecurityTokenHandler().ReadJwtToken(data.GetProperty("accessToken").GetString());
        Assert.Contains(token.Claims, c => c.Type == "is_super_admin" && c.Value == "true");
        Assert.Contains(token.Claims, c => c.Type == "tenantId");
        Assert.Contains(token.Claims, c => c.Type == "permission" && c.Value == "USER_VIEW");
    }

    [Fact]
    public async Task Wrong_credentials_give_the_same_answer_whether_or_not_the_email_exists()
    {
        var client = factory.CreateClient();

        var wrongPassword = await client.TryLoginAsync(ApiFactory.AdminEmail, "not-the-password");
        var unknownEmail = await client.TryLoginAsync("nobody@test.local", "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await wrongPassword.MessageAsync(), await unknownEmail.MessageAsync());
    }

    [Fact]
    public void Passwords_are_stored_hashed_never_as_typed()
    {
        var hash = factory.Scalar<string>("SELECT PasswordHash FROM auth.UserAccounts WHERE Email = @e", ("@e", ApiFactory.AdminEmail));

        Assert.NotEqual(ApiFactory.AdminPassword, hash);
        Assert.DoesNotContain(ApiFactory.AdminPassword, hash);
        Assert.True(hash.Length > 40);
    }

    [Fact]
    public async Task Refresh_issues_a_new_pair_and_a_replayed_token_ends_every_session()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var email = $"refresh.{Guid.NewGuid():N}@test.local";
        await admin.CreateActiveUserAsync(email, Password);
        var client = factory.CreateClient();
        var first = await client.LoginAsync(email, Password);

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.DataAsync()).GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first.RefreshToken, second);

        // The first token is spent. Using it again looks like theft, so the newer token dies too.
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var afterReplay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = second });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task Sign_out_revokes_the_refresh_token()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var email = $"logout.{Guid.NewGuid():N}@test.local";
        await admin.CreateActiveUserAsync(email, Password);
        var client = factory.CreateClient();
        var session = await client.LoginAsync(email, Password);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_right_password()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var email = $"lockout.{Guid.NewGuid():N}@test.local";
        await admin.CreateActiveUserAsync(email, Password);
        var client = factory.CreateClient();

        // The fifth failure is still an ordinary 401, but it is the one that sets the lock.
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.TryLoginAsync(email, "wrong")).StatusCode);

        var locked = await client.TryLoginAsync(email, "wrong");
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.RetryAfter is not null);

        var correct = await client.TryLoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
    }

    [Fact]
    public async Task A_weak_password_is_refused_when_accepting_an_invite()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var roleId = await admin.RoleIdAsync("STAFF");
        var created = await admin.PostAsJsonAsync("/api/users", new { firstName = "Weak", email = $"weak.{Guid.NewGuid():N}@test.local", roleId });
        var link = (await created.DataAsync()).GetProperty("inviteLink").GetString()!;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/accept-invite", new { token, newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
