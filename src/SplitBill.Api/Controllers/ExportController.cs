using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Export;

namespace SplitBill.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ExportController : ControllerBase
{
    // UTF-8 kèm BOM để Excel mở trực tiếp hiển thị đúng tiếng Việt có dấu.
    private static readonly UTF8Encoding CsvEncoding = new(encoderShouldEmitUTF8Identifier: true);

    private readonly IExportService _exportService;

    public ExportController(IExportService exportService)
    {
        _exportService = exportService;
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/export/expenses.csv")]
    public async Task<IActionResult> ExportExpensesAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var csv = await _exportService.ExportExpensesCsvAsync(User.GetUserId(), groupId, cancellationToken);
        return File(CsvEncoding.GetBytes(csv), "text/csv", $"khoan-chi-{groupId}.csv");
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/export/balances.csv")]
    public async Task<IActionResult> ExportBalancesAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var csv = await _exportService.ExportBalancesCsvAsync(User.GetUserId(), groupId, cancellationToken);
        return File(CsvEncoding.GetBytes(csv), "text/csv", $"so-du-{groupId}.csv");
    }
}
