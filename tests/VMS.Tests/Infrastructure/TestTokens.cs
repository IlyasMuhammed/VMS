using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VMS.Shared.Common;

namespace VMS.Tests.Infrastructure;

/// <summary>
/// Signs an access token for a user who holds exactly the permissions a test names, without creating
/// the user. The API only checks the signature and reads the claims, which is what makes this a
/// faithful stand-in for a real sign-in.
/// </summary>
public static class TestTokens
{
    public static string For(string userName, int userId, params string[] permissions) =>
        For(TenantDefaults.PlatformTenantId, userName, userId, permissions);

    public static string For(Guid tenantId, string userName, int userId, params string[] permissions) =>
        ForScoped(tenantId, userName, userId, scopeType: null, branchId: null, linkedPartnerId: null, permissions);

    /// <summary>Like <see cref="For(Guid,string,int,string[])"/>, but with a data scope (§23B.4) baked into the token, for testing scope filters without creating a real user.</summary>
    public static string ForScoped(Guid tenantId, string userName, int userId, string? scopeType, Guid? branchId, int? linkedPartnerId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenantId", tenantId.ToString()),
            new("is_super_admin", "false"),
            new("user_name", userName),
        };
        if (scopeType is not null) claims.Add(new Claim("scope", scopeType));
        if (branchId is { } b) claims.Add(new Claim("scopeBranchId", b.ToString()));
        if (linkedPartnerId is { } p) claims.Add(new Claim("linkedPartnerId", p.ToString()));
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.Secret));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
