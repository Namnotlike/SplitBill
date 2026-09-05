using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Expenses; // PagedResult<T>
using SplitBill.Application.Notifications;

namespace SplitBill.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private const int DefaultPageSize = 20;

    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> GetPagedAsync(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, 100);

        var result = await _notificationService.GetPagedAsync(User.GetUserId(), page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        var count = await _notificationService.GetUnreadCountAsync(User.GetUserId(), cancellationToken);
        return Ok(new UnreadCountDto(count));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsReadAsync(Guid id, CancellationToken cancellationToken)
    {
        await _notificationService.MarkAsReadAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsReadAsync(CancellationToken cancellationToken)
    {
        await _notificationService.MarkAllAsReadAsync(User.GetUserId(), cancellationToken);
        return NoContent();
    }
}
