using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Notifications;
using SplitBill.Application.Settlements;
using SplitBill.Application.Users;

namespace SplitBill.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IBalanceService _balanceService;
    private readonly IUserDashboardService _dashboardService;
    private readonly INotificationService _notificationService;
    private readonly IGlobalSearchService _searchService;
    private readonly ITwoFactorService _twoFactorService;
    private readonly IValidator<CreatePushSubscriptionRequest> _pushSubscriptionValidator;
    private readonly IValidator<EnableTwoFactorRequest> _enableTwoFactorValidator;
    private readonly IValidator<DisableTwoFactorRequest> _disableTwoFactorValidator;
    private readonly IValidator<RegenerateRecoveryCodesRequest> _regenerateRecoveryCodesValidator;

    public UsersController(
        IUserService userService,
        IBalanceService balanceService,
        IUserDashboardService dashboardService,
        INotificationService notificationService,
        IGlobalSearchService searchService,
        ITwoFactorService twoFactorService,
        IValidator<CreatePushSubscriptionRequest> pushSubscriptionValidator,
        IValidator<EnableTwoFactorRequest> enableTwoFactorValidator,
        IValidator<DisableTwoFactorRequest> disableTwoFactorValidator,
        IValidator<RegenerateRecoveryCodesRequest> regenerateRecoveryCodesValidator)
    {
        _userService = userService;
        _balanceService = balanceService;
        _dashboardService = dashboardService;
        _notificationService = notificationService;
        _searchService = searchService;
        _twoFactorService = twoFactorService;
        _pushSubscriptionValidator = pushSubscriptionValidator;
        _enableTwoFactorValidator = enableTwoFactorValidator;
        _disableTwoFactorValidator = disableTwoFactorValidator;
        _regenerateRecoveryCodesValidator = regenerateRecoveryCodesValidator;
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMeAsync(CancellationToken cancellationToken)
    {
        var profile = await _userService.GetProfileAsync(User.GetUserId(), cancellationToken);
        return Ok(profile);
    }

    /// <summary>Bảng tổng quan cá nhân ở trang chủ (CLAUDE.md mục 15.4) — số dư của user hiện tại
    /// trong TỪNG nhóm họ đang tham gia.</summary>
    [HttpGet("me/balances-overview")]
    public async Task<ActionResult<IReadOnlyList<PersonalGroupBalanceDto>>> GetMyBalancesOverviewAsync(CancellationToken cancellationToken)
    {
        var overview = await _balanceService.GetMyOverviewAsync(User.GetUserId(), cancellationToken);
        return Ok(overview);
    }

    /// <summary>"Ai đang nợ tôi / tôi đang nợ ai" gộp theo từng người, xuyên mọi nhóm (CLAUDE.md mục 20).</summary>
    [HttpGet("me/counterparty-balances")]
    public async Task<ActionResult<IReadOnlyList<CounterpartyBalanceDto>>> GetMyCounterpartyBalancesAsync(CancellationToken cancellationToken)
    {
        var balances = await _balanceService.GetCounterpartyBalancesAsync(User.GetUserId(), cancellationToken);
        return Ok(balances);
    }

    /// <summary>Dashboard cá nhân nâng cao (CLAUDE.md mục 25.5) — settlement đang chờ mình xác nhận +
    /// hoạt động gần đây xuyên mọi nhóm.</summary>
    [HttpGet("me/dashboard")]
    public async Task<ActionResult<PersonalDashboardDto>> GetMyDashboardAsync(CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardService.GetDashboardAsync(User.GetUserId(), cancellationToken);
        return Ok(dashboard);
    }

    [HttpPatch("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMeAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _userService.UpdateProfileAsync(User.GetUserId(), request, cancellationToken);
        return Ok(profile);
    }

    // ===== Web Push (CLAUDE.md mục 25.7) =====

    /// <summary>VAPID public key để client subscribe — rỗng nếu chưa cấu hình (tính năng tùy chọn).
    /// Giá trị không phải bí mật (đúng thiết kế VAPID — public key được gửi thẳng cho trình duyệt),
    /// nhưng vẫn đặt dưới /users/me nên vẫn yêu cầu JWT như mọi endpoint khác cùng nhóm (CLAUDE.md
    /// mục 8: "Toàn bộ endpoint dưới /users/me yêu cầu JWT Bearer hợp lệ").</summary>
    [HttpGet("me/push-vapid-public-key")]
    public ActionResult<VapidPublicKeyDto> GetVapidPublicKey() => Ok(new VapidPublicKeyDto(_notificationService.GetVapidPublicKey()));

    [HttpPost("me/push-subscriptions")]
    public async Task<IActionResult> SubscribeToPushAsync(CreatePushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        _pushSubscriptionValidator.ValidateOrThrowDomainException(request);
        await _notificationService.SubscribeToPushAsync(User.GetUserId(), request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("me/push-subscriptions")]
    public async Task<IActionResult> UnsubscribeFromPushAsync([FromQuery] string endpoint, CancellationToken cancellationToken)
    {
        await _notificationService.UnsubscribeFromPushAsync(User.GetUserId(), endpoint, cancellationToken);
        return NoContent();
    }

    /// <summary>Tìm kiếm xuyên nhóm (CLAUDE.md mục 25.8) — khớp tên nhóm + tiêu đề khoản chi trên
    /// TOÀN BỘ nhóm caller đang tham gia. Query rỗng/thiếu trả về 2 danh sách rỗng, không phải lỗi.</summary>
    [HttpGet("me/search")]
    public async Task<ActionResult<GlobalSearchResultDto>> SearchAsync([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var result = await _searchService.SearchAsync(User.GetUserId(), q, cancellationToken);
        return Ok(result);
    }

    // ===== Xác thực 2 lớp / TOTP (CLAUDE.md mục 25.9) =====

    [HttpGet("me/2fa/status")]
    public async Task<ActionResult<TwoFactorStatusDto>> GetTwoFactorStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _twoFactorService.GetStatusAsync(User.GetUserId(), cancellationToken);
        return Ok(status);
    }

    [HttpPost("me/2fa/setup")]
    public async Task<ActionResult<TwoFactorSetupDto>> SetupTwoFactorAsync(CancellationToken cancellationToken)
    {
        var setup = await _twoFactorService.SetupAsync(User.GetUserId(), cancellationToken);
        return Ok(setup);
    }

    [HttpPost("me/2fa/enable")]
    public async Task<ActionResult<RecoveryCodesDto>> EnableTwoFactorAsync(EnableTwoFactorRequest request, CancellationToken cancellationToken)
    {
        _enableTwoFactorValidator.ValidateOrThrowDomainException(request);
        var result = await _twoFactorService.EnableAsync(User.GetUserId(), request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("me/2fa/disable")]
    public async Task<IActionResult> DisableTwoFactorAsync(DisableTwoFactorRequest request, CancellationToken cancellationToken)
    {
        _disableTwoFactorValidator.ValidateOrThrowDomainException(request);
        await _twoFactorService.DisableAsync(User.GetUserId(), request, cancellationToken);
        return NoContent();
    }

    [HttpPost("me/2fa/recovery-codes/regenerate")]
    public async Task<ActionResult<RecoveryCodesDto>> RegenerateTwoFactorRecoveryCodesAsync(RegenerateRecoveryCodesRequest request, CancellationToken cancellationToken)
    {
        _regenerateRecoveryCodesValidator.ValidateOrThrowDomainException(request);
        var result = await _twoFactorService.RegenerateRecoveryCodesAsync(User.GetUserId(), request, cancellationToken);
        return Ok(result);
    }
}
