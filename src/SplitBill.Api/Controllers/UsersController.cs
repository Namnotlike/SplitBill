using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Users;

namespace SplitBill.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMeAsync(CancellationToken cancellationToken)
    {
        var profile = await _userService.GetProfileAsync(User.GetUserId(), cancellationToken);
        return Ok(profile);
    }

    [HttpPatch("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMeAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _userService.UpdateProfileAsync(User.GetUserId(), request, cancellationToken);
        return Ok(profile);
    }
}
