using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;

namespace SplitBill.Application.Export;

/// <summary>
/// Xuất/backup toàn bộ dữ liệu 1 nhóm dạng JSON (CLAUDE.md mục 25.4, bổ sung 2026-09-09).
///
/// Phạm vi CỐ Ý giới hạn ở dữ liệu TÀI CHÍNH cốt lõi — đủ để biết "nhóm này gồm ai, đã chi những gì,
/// đã thanh toán những gì" — KHÔNG bao gồm: <c>ReceiptImage</c> (ảnh nhị phân, base64 hóa sẽ làm file
/// backup phình to bất hợp lý so với giá trị mang lại), <c>AuditLog</c> (nhật ký kỹ thuật, không phải
/// dữ liệu cần khôi phục), <c>ExpenseComment</c> (nội dung trao đổi xã hội, không phải số liệu tài
/// chính), <c>RecurringExpenseTemplate</c>/<c>SplitPreset</c> (cấu hình tiện ích, không phải lịch sử đã
/// xảy ra). Đây là ranh giới có chủ đích, cùng tinh thần các quyết định phạm vi đã ghi ở mục 18 (Nhân
/// bản nhóm) — không phải thiếu sót.
/// </summary>
public sealed record GroupBackupDto(
    DateTimeOffset ExportedAt,
    GroupDto Group,
    IReadOnlyList<ExpenseDto> Expenses,
    IReadOnlyList<SettlementDto> Settlements);
