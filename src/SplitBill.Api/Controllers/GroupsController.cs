using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses; // PagedResult<T>
using SplitBill.Application.Groups;

namespace SplitBill.Api.Controllers;

[ApiController]
[Route("api/v1/groups")]
[Authorize]
public sealed class GroupsController : ControllerBase
{
    private const int DefaultAuditLogPageSize = 20;

    private readonly IGroupService _groupService;
    private readonly IValidator<CreateGroupRequest> _createGroupValidator;
    private readonly IValidator<AddMemberRequest> _addMemberValidator;
    private readonly IValidator<UpdateMemberRequest> _updateMemberValidator;
    private readonly IValidator<UpdateMemberRoleRequest> _updateMemberRoleValidator;

    public GroupsController(
        IGroupService groupService,
        IValidator<CreateGroupRequest> createGroupValidator,
        IValidator<AddMemberRequest> addMemberValidator,
        IValidator<UpdateMemberRequest> updateMemberValidator,
        IValidator<UpdateMemberRoleRequest> updateMemberRoleValidator)
    {
        _groupService = groupService;
        _createGroupValidator = createGroupValidator;
        _addMemberValidator = addMemberValidator;
        _updateMemberValidator = updateMemberValidator;
        _updateMemberRoleValidator = updateMemberRoleValidator;
    }

    [HttpPost]
    public async Task<ActionResult<GroupDto>> CreateAsync(CreateGroupRequest request, CancellationToken cancellationToken)
    {
        _createGroupValidator.ValidateOrThrowDomainException(request);
        var group = await _groupService.CreateAsync(User.GetUserId(), request, cancellationToken);
        // Lưu ý: ASP.NET Core mặc định strip hậu tố "Async" khỏi ActionName (SuppressAsyncSuffixInActionNames
        // = false), nên route đã đăng ký tên là "GetById", không phải "GetByIdAsync" như nameof() trả về.
        return CreatedAtAction("GetById", new { id = group.Id }, group);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GroupSummaryDto>>> GetMyGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await _groupService.GetMyGroupsAsync(User.GetUserId(), cancellationToken);
        return Ok(groups);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GroupDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await _groupService.GetByIdAsync(User.GetUserId(), id, cancellationToken);
        return Ok(group);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GroupDto>> UpdateAsync(Guid id, UpdateGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await _groupService.UpdateAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(group);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _groupService.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpGet("shared/{shareToken}")]
    [AllowAnonymous]
    public async Task<ActionResult<GroupDto>> GetBySharedTokenAsync(string shareToken, CancellationToken cancellationToken)
    {
        var group = await _groupService.GetBySharedTokenAsync(shareToken, cancellationToken);
        return Ok(group);
    }

    [HttpPost("{id:guid}/share-token/rotate")]
    public async Task<ActionResult<object>> RotateShareTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        var newToken = await _groupService.RotateShareTokenAsync(User.GetUserId(), id, cancellationToken);
        return Ok(new { shareToken = newToken });
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<GroupMemberDto>> AddMemberAsync(Guid id, AddMemberRequest request, CancellationToken cancellationToken)
    {
        _addMemberValidator.ValidateOrThrowDomainException(request);
        var member = await _groupService.AddMemberAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(member);
    }

    [HttpPatch("{id:guid}/members/{memberId:guid}")]
    public async Task<ActionResult<GroupMemberDto>> UpdateMemberAsync(Guid id, Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        _updateMemberValidator.ValidateOrThrowDomainException(request);
        var member = await _groupService.UpdateMemberAsync(User.GetUserId(), id, memberId, request, cancellationToken);
        return Ok(member);
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMemberAsync(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        await _groupService.RemoveMemberAsync(User.GetUserId(), id, memberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/members/{memberId:guid}/role")]
    public async Task<ActionResult<GroupMemberDto>> UpdateMemberRoleAsync(Guid id, Guid memberId, UpdateMemberRoleRequest request, CancellationToken cancellationToken)
    {
        _updateMemberRoleValidator.ValidateOrThrowDomainException(request);
        var member = await _groupService.UpdateMemberRoleAsync(User.GetUserId(), id, memberId, request, cancellationToken);
        return Ok(member);
    }

    [HttpGet("{id:guid}/audit-logs")]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> GetAuditLogsAsync(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultAuditLogPageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultAuditLogPageSize : Math.Min(pageSize, 100);

        var result = await _groupService.GetAuditLogsAsync(User.GetUserId(), id, page, pageSize, cancellationToken);
        return Ok(result);
    }
}
