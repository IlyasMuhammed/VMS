using VMS.Modules.Auth.Domain;

namespace VMS.Modules.Auth.Services;

internal interface ITokenService
{
    string GenerateAccessToken(UserAccount user, string roleName, IReadOnlyCollection<string> permissions, bool isSuperAdmin, string scopeType, Guid? scopeBranchId);
    string GenerateRefreshToken();
}
