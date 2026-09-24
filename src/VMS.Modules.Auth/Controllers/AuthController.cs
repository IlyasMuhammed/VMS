using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VMS.Modules.Auth.Models;
using VMS.Modules.Auth.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Auth.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    public const string RateLimitPolicy = "auth-per-ip";

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestModel request) =>
        Ok(ApiResponse<LoginResponseModel>.Ok(await auth.LoginAsync(request)));

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestModel request) =>
        Ok(ApiResponse<RefreshResponseModel>.Ok(await auth.RefreshAsync(request.RefreshToken)));

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestModel request)
    {
        await auth.LogoutAsync(request.RefreshToken);
        return Ok(ApiResponse.Ok("Logged out."));
    }

    [AuthenticatedOnly]
    [HttpGet("me")]
    public async Task<IActionResult> Me() =>
        Ok(ApiResponse<CurrentUserModel>.Ok(await auth.GetCurrentUserAsync(User.GetUserId())));

    [AuthenticatedOnly]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await auth.ChangePasswordAsync(User.GetUserId(), request);
        return Ok(ApiResponse.Ok("Password changed. Please sign in again."));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await auth.ForgotPasswordAsync(request.Email);
        // Same answer whether or not the email has an account.
        return Ok(ApiResponse.Ok("If an account exists for that email, a reset code has been sent."));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await auth.ResetPasswordAsync(request);
        return Ok(ApiResponse.Ok("Your password has been updated."));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("accept-invite")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
    {
        await auth.AcceptInviteAsync(request.Token, request.NewPassword);
        return Ok(ApiResponse.Ok("Password set — you can now sign in."));
    }
}
