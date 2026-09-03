using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Settlements;

namespace SplitBill.Api.Controllers;

[ApiController]
[Authorize]
public sealed class SettlementsController : ControllerBase
{
    private readonly IBalanceService _balanceService;
    private readonly ISettlementRecordService _settlementService;
    private readonly IValidator<CreateSettlementRequest> _createValidator;

    public SettlementsController(
        IBalanceService balanceService,
        ISettlementRecordService settlementService,
        IValidator<CreateSettlementRequest> createValidator)
    {
        _balanceService = balanceService;
        _settlementService = settlementService;
        _createValidator = createValidator;
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/balances")]
    public async Task<ActionResult<IReadOnlyList<MemberBalanceDto>>> GetBalancesAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var balances = await _balanceService.GetBalancesAsync(User.GetUserId(), groupId, cancellationToken);
        return Ok(balances);
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/settlement-plan")]
    public async Task<ActionResult<SettlementPlanDto>> GetSettlementPlanAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var plan = await _balanceService.GetSettlementPlanAsync(User.GetUserId(), groupId, cancellationToken);
        return Ok(plan);
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/settlements")]
    public async Task<ActionResult<IReadOnlyList<SettlementDto>>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var settlements = await _settlementService.GetByGroupAsync(User.GetUserId(), groupId, cancellationToken);
        return Ok(settlements);
    }

    [HttpPost("/api/v1/groups/{groupId:guid}/settlements")]
    public async Task<ActionResult<SettlementDto>> CreateAsync(Guid groupId, CreateSettlementRequest request, CancellationToken cancellationToken)
    {
        _createValidator.ValidateOrThrowDomainException(request);
        var settlement = await _settlementService.CreateAsync(User.GetUserId(), groupId, request, cancellationToken);
        return Ok(settlement);
    }

    [HttpPost("/api/v1/settlements/{settlementId:guid}/confirm")]
    public async Task<ActionResult<SettlementDto>> ConfirmAsync(Guid settlementId, CancellationToken cancellationToken)
    {
        var settlement = await _settlementService.ConfirmAsync(User.GetUserId(), settlementId, cancellationToken);
        return Ok(settlement);
    }

    [HttpPost("/api/v1/settlements/{settlementId:guid}/reject")]
    public async Task<ActionResult<SettlementDto>> RejectAsync(Guid settlementId, CancellationToken cancellationToken)
    {
        var settlement = await _settlementService.RejectAsync(User.GetUserId(), settlementId, cancellationToken);
        return Ok(settlement);
    }

    [HttpDelete("/api/v1/settlements/{settlementId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid settlementId, CancellationToken cancellationToken)
    {
        await _settlementService.DeleteAsync(User.GetUserId(), settlementId, cancellationToken);
        return NoContent();
    }
}
