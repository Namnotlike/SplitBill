using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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

    // WriteIndented để file backup.json vẫn đọc được bằng mắt (mục đích chính là backup/khôi phục
    // thủ công, khác các response API bình thường vốn ưu tiên gọn nhẹ). Encoder mặc định của
    // System.Text.Json escape ký tự ngoài ASCII (kể cả tiếng Việt có dấu) thành \uXXXX — dùng
    // UnsafeRelaxedJsonEscaping để giữ nguyên văn tiếng Việt đọc được trực tiếp trong file (an toàn ở
    // đây vì đây là file tải xuống, không phải HTML render lại — không có rủi ro XSS).
    private static readonly JsonSerializerOptions BackupJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

    // CLAUDE.md mục 25.4 — Xuất/backup toàn bộ dữ liệu tài chính cốt lõi của 1 nhóm dạng JSON.
    [HttpGet("/api/v1/groups/{groupId:guid}/export/backup.json")]
    public async Task<IActionResult> ExportGroupBackupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var backup = await _exportService.ExportGroupBackupAsync(User.GetUserId(), groupId, cancellationToken);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, BackupJsonOptions);
        return File(bytes, "application/json", $"backup-{groupId}.json");
    }
}
