using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.RecurringExpenses;

namespace SplitBill.Api.Controllers;

/// <summary>CLAUDE.md mục 15.7 — Khoản chi định kỳ.</summary>
[ApiController]
[Authorize]
public sealed class RecurringExpensesController : ControllerBase
{
    private readonly IRecurringExpenseService _recurringExpenseService;

    public RecurringExpensesController(IRecurringExpenseService recurringExpenseService)
    {
        _recurringExpenseService = recurringExpenseService;
    }

    [HttpPost("/api/v1/groups/{groupId:guid}/recurring-expenses")]
    public async Task<ActionResult<RecurringExpenseTemplateDto>> CreateAsync(Guid groupId, CreateRecurringExpenseRequest request, CancellationToken cancellationToken)
    {
        var template = await _recurringExpenseService.CreateAsync(User.GetUserId(), groupId, request, cancellationToken);
        return Ok(template);
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/recurring-expenses")]
    public async Task<ActionResult<IReadOnlyList<RecurringExpenseTemplateDto>>> GetByGroupIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var templates = await _recurringExpenseService.GetByGroupIdAsync(User.GetUserId(), groupId, cancellationToken);
        return Ok(templates);
    }

    [HttpPost("/api/v1/recurring-expenses/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await _recurringExpenseService.DeactivateAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }
}
