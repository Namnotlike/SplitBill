using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;

namespace SplitBill.Api.Controllers;

/// <summary>CLAUDE.md mục 19 — Bình luận trên khoản chi.</summary>
[ApiController]
[Authorize]
public sealed class ExpenseCommentsController : ControllerBase
{
    private readonly IExpenseCommentService _commentService;
    private readonly IValidator<CreateExpenseCommentRequest> _createValidator;

    public ExpenseCommentsController(IExpenseCommentService commentService, IValidator<CreateExpenseCommentRequest> createValidator)
    {
        _commentService = commentService;
        _createValidator = createValidator;
    }

    [HttpGet("/api/v1/expenses/{expenseId:guid}/comments")]
    public async Task<ActionResult<IReadOnlyList<ExpenseCommentDto>>> GetByExpenseIdAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        var comments = await _commentService.GetByExpenseIdAsync(User.GetUserId(), expenseId, cancellationToken);
        return Ok(comments);
    }

    [HttpPost("/api/v1/expenses/{expenseId:guid}/comments")]
    public async Task<ActionResult<ExpenseCommentDto>> CreateAsync(Guid expenseId, CreateExpenseCommentRequest request, CancellationToken cancellationToken)
    {
        _createValidator.ValidateOrThrowDomainException(request);
        var comment = await _commentService.CreateAsync(User.GetUserId(), expenseId, request, cancellationToken);
        return Ok(comment);
    }

    [HttpDelete("/api/v1/expense-comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid commentId, CancellationToken cancellationToken)
    {
        await _commentService.DeleteAsync(User.GetUserId(), commentId, cancellationToken);
        return NoContent();
    }
}
