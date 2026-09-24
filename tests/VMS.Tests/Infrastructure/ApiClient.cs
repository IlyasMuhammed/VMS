using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace VMS.Tests.Infrastructure;

public sealed record Session(string AccessToken, string RefreshToken);

/// <summary>Small helpers so tests read as what they check, not as HTTP plumbing.</summary>
public static class ApiClient
{
    public static async Task<JsonElement> DataAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default;
    }

    public static async Task<string> MessageAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
    }

    /// <summary>PATCH with a JSON body (System.Net.Http.Json has no PATCH helper before .NET 10).</summary>
    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PatchAsync(url, JsonContent.Create(body));

    public static HttpClient WithToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static async Task<HttpResponseMessage> TryLoginAsync(this HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static async Task<Session> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.TryLoginAsync(email, password);
        response.EnsureSuccessStatusCode();
        var data = await response.DataAsync();
        return new Session(data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>An HTTP client that is already signed in as the seeded Super Admin.</summary>
    public static async Task<HttpClient> AsSuperAdminAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        var session = await client.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        return client.WithToken(session.AccessToken);
    }

    /// <summary>The id of a seeded global role by its code (for example STAFF).</summary>
    public static async Task<int> RoleIdAsync(this HttpClient admin, string roleCode)
    {
        var roles = await (await admin.GetAsync("/api/users/assignable-roles")).DataAsync();
        return roles.EnumerateArray().First(r => r.GetProperty("roleCode").GetString() == roleCode).GetProperty("roleId").GetInt32();
    }

    /// <summary>
    /// Creates a user in the caller's tenant and completes their invite, so they can sign in with the
    /// given password. Returns the new user's id.
    /// </summary>
    public static async Task<int> CreateActiveUserAsync(this HttpClient admin, string email, string password, string roleCode = "STAFF")
    {
        var roleId = await admin.RoleIdAsync(roleCode);
        var created = await admin.PostAsJsonAsync("/api/users", new { firstName = "Test", lastName = "User", email, roleId });
        created.EnsureSuccessStatusCode();
        var user = await created.DataAsync();

        var link = user.GetProperty("inviteLink").GetString()!;
        var token = HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;
        var accepted = await admin.PostAsJsonAsync("/api/auth/accept-invite", new { token, newPassword = password });
        accepted.EnsureSuccessStatusCode();
        return user.GetProperty("userId").GetInt32();
    }
}
