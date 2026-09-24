using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Infrastructure;
using VMS.Shared.Common;

namespace VMS.Modules.Auth.Services;

internal sealed class TokenService(IOptions<AppSettings> settings) : ITokenService
{
    private const int AccessTokenMinutes = 30;

    /// <summary>
    /// How long an access token lives, in seconds — the value <c>expiresIn</c> must carry. Derived from
    /// the lifetime here so login and refresh responses can never disagree about it.
    /// </summary>
    internal const int AccessTokenSeconds = AccessTokenMinutes * 60;

    public string GenerateAccessToken(UserAccount user, string roleName, IReadOnlyCollection<string> permissions, bool isSuperAdmin, string scopeType, Guid? scopeBranchId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Value.Secret));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserID.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("roleId", user.RoleID.ToString()),
            new("roleName", roleName),
            // Shown in the audit trail and the access-denial log next to the user id.
            new("user_name", string.Join(' ', new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))),
            new("tenantId", user.TenantId.ToString()),
            new("is_super_admin", isSuperAdmin ? "true" : "false"),
            // §23B.4: the widest scope across every role the user holds. Read by ICallerScope, not by this module.
            new("scope", scopeType),
        };
        if (scopeBranchId is { } branch) claims.Add(new Claim("scopeBranchId", branch.ToString()));
        if (user.LinkedPartnerId is { } partnerId) claims.Add(new Claim("linkedPartnerId", partnerId.ToString()));
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public string GenerateRefreshToken() => TokenHelper.NewToken();
}
