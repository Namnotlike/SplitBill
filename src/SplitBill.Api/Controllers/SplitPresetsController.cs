using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;

namespace SplitBill.Api.Controllers;

/// <summary>CLAUDE.md mục 21 — Preset cách chia hay dùng.</summary>
[ApiController]
[Authorize]
public sealed class SplitPresetsController : ControllerBase
{
    private readonly ISplitPresetService _presetService;
    private readonly IValidator<CreateSplitPresetRequest> _createValidator;

    public SplitPresetsController(ISplitPresetService presetService, IValidator<CreateSplitPresetRequest> createValidator)
    {
        _presetService = presetService;
        _createValidator = createValidator;
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/split-presets")]
    public async Task<ActionResult<IReadOnlyList<SplitPresetDto>>> GetByGroupIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var presets = await _presetService.GetByGroupIdAsync(User.GetUserId(), groupId, cancellationToken);
        return Ok(presets);
    }

    [HttpPost("/api/v1/groups/{groupId:guid}/split-presets")]
    public async Task<ActionResult<SplitPresetDto>> CreateAsync(Guid groupId, CreateSplitPresetRequest request, CancellationToken cancellationToken)
    {
        _createValidator.ValidateOrThrowDomainException(request);
        var preset = await _presetService.CreateAsync(User.GetUserId(), groupId, request, cancellationToken);
        return Ok(preset);
    }

    [HttpDelete("/api/v1/split-presets/{presetId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid presetId, CancellationToken cancellationToken)
    {
        await _presetService.DeleteAsync(User.GetUserId(), presetId, cancellationToken);
        return NoContent();
    }
}
