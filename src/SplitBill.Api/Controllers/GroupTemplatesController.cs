using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Groups;

namespace SplitBill.Api.Controllers;

/// <summary>CLAUDE.md mục 25.6 — Mẫu nhóm tái sử dụng.</summary>
[ApiController]
[Authorize]
[Route("/api/v1/group-templates")]
public sealed class GroupTemplatesController : ControllerBase
{
    private readonly IGroupTemplateService _templateService;
    private readonly IValidator<CreateGroupTemplateRequest> _createValidator;

    public GroupTemplatesController(IGroupTemplateService templateService, IValidator<CreateGroupTemplateRequest> createValidator)
    {
        _templateService = templateService;
        _createValidator = createValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GroupTemplateDto>>> GetMyTemplatesAsync(CancellationToken cancellationToken)
    {
        var templates = await _templateService.GetMyTemplatesAsync(User.GetUserId(), cancellationToken);
        return Ok(templates);
    }

    [HttpPost]
    public async Task<ActionResult<GroupTemplateDto>> CreateAsync(CreateGroupTemplateRequest request, CancellationToken cancellationToken)
    {
        _createValidator.ValidateOrThrowDomainException(request);
        var template = await _templateService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return Ok(template);
    }

    [HttpDelete("{templateId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid templateId, CancellationToken cancellationToken)
    {
        await _templateService.DeleteAsync(User.GetUserId(), templateId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{templateId:guid}/create-group")]
    public async Task<ActionResult<GroupDto>> CreateGroupFromTemplateAsync(Guid templateId, CreateGroupFromTemplateRequest request, CancellationToken cancellationToken)
    {
        var group = await _templateService.CreateGroupFromTemplateAsync(User.GetUserId(), templateId, request, cancellationToken);
        return Ok(group);
    }
}
