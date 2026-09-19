using VMS.Modules.Auth.Models;

namespace VMS.Modules.Auth.Services;

public interface IAuthService
{
    Task<LoginResponseModel> LoginAsync(LoginRequestModel request);
    Task<RefreshResponseModel> RefreshAsync(string rawRefreshToken);
    Task LogoutAsync(string rawRefreshToken);
    Task<CurrentUserModel> GetCurrentUserAsync(int userId);
    Task ChangePasswordAsync(int userId, ChangePasswordRequest request);
    Task ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task AcceptInviteAsync(string token, string newPassword);
}
