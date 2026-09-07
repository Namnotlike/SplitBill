using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
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

    public UsersController(IUserService userService, IBalanceService balanceService)
    {
        _userService = userService;
        _balanceService = balanceService;
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

    [HttpPatch("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMeAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _userService.UpdateProfileAsync(User.GetUserId(), request, cancellationToken);
        return Ok(profile);
    }
}
