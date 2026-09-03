using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitBill.Infrastructure.Persistence;

namespace SplitBill.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly SplitBillDbContext _dbContext;

    public HealthController(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Health check — trả 200 nếu kết nối DB OK (CLAUDE.md mục 8).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unhealthy", database = "unreachable" });
        }

        return Ok(new { status = "healthy" });
    }
}
