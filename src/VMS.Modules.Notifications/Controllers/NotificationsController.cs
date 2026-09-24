using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Notifications.Models;
using VMS.Modules.Notifications.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Notifications.Controllers;

/// <summary>The signed-in user's own in-app notification list (S7-NOT-06, FSD §23.4): the shell's bell icon and its dropdown.</summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List([FromQuery] bool unreadOnly = false, [FromQuery] int take = 50) =>
        Ok(ApiResponse<List<NotificationModel>>.Ok(await notifications.ListAsync(User.GetUserId(), unreadOnly, take)));

    [HttpGet("unread-count")]
    [AuthenticatedOnly]
    public async Task<IActionResult> UnreadCount() => Ok(ApiResponse<UnreadCountModel>.Ok(new UnreadCountModel { Count = await notifications.UnreadCountAsync(User.GetUserId()) }));

    [HttpPost("{id:int}/read")]
    [AuthenticatedOnly]
    public async Task<IActionResult> MarkRead(int id)
    {
        await notifications.MarkReadAsync(User.GetUserId(), id);
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("read-all")]
    [AuthenticatedOnly]
    public async Task<IActionResult> MarkAllRead()
    {
        await notifications.MarkAllReadAsync(User.GetUserId());
        return Ok(ApiResponse.Ok());
    }
}

/// <summary>The notification rule master (FSD §9), configurable under Administration (S7-NOT-05).</summary>
[ApiController]
[Route("api/admin/notification-rules")]
public class NotificationRulesController(INotificationRuleService rules) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.ADM_NOTIFICATION_MANAGE)]
    public async Task<IActionResult> List() => Ok(ApiResponse<List<NotificationRuleModel>>.Ok(await rules.ListAsync()));

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionCodes.ADM_NOTIFICATION_MANAGE)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveNotificationRuleRequest request) =>
        Ok(ApiResponse<NotificationRuleModel>.Ok(await rules.UpdateAsync(id, request), "Notification rule saved."));
}

/// <summary>A manual trigger for the notification evaluation job (S7-NOT-03), the same pattern S4-REC-05 and S5-DOC-06/09 set for a job with no scheduler.</summary>
[ApiController]
[Route("api/admin/jobs/notifications")]
public class NotificationJobController(INotificationJobsRunner runner) : ControllerBase
{
    [HttpPost("run")]
    [RequirePermission(PermissionCodes.ADM_CONFIG_MANAGE)]
    public async Task<IActionResult> Run() => Ok(ApiResponse<NotificationEvaluationResult>.Ok(await runner.RunAsync()));
}
