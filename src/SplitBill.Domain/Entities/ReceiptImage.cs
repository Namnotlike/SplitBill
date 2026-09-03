namespace SplitBill.Domain.Entities;

/// <summary>
/// Ảnh hóa đơn, lưu thẳng trong SQL Server (varbinary(max)) — không dùng local disk hay cloud
/// storage riêng (quyết định người dùng, xem CLAUDE.md mục 4.1). Mỗi Expense chỉ giữ 1 ảnh gần
/// nhất; upload lại thì ghi đè.
/// </summary>
public sealed class ReceiptImage
{
    public Guid Id { get; set; }
    public Guid ExpenseId { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Expense? Expense { get; set; }
}
