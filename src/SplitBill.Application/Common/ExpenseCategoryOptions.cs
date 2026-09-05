namespace SplitBill.Application.Common;

/// <summary>Nhãn hiển thị (tiếng Việt + icon) cho từng <c>ExpenseCategory</c> (CLAUDE.md mục 15.3).
/// Đặt ở Application để Web dùng lại được (Web tham chiếu Application chỉ để lấy DTO/hằng số dùng
/// chung, không gọi service in-process — CLAUDE.md mục 10b), tránh khai báo trùng danh sách nhãn ở
/// nhiều trang Razor (Create/Edit/Index đều cần cùng một danh sách).</summary>
public static class ExpenseCategoryOptions
{
    public sealed record Info(string Value, string Label, string Icon);

    public static readonly IReadOnlyList<Info> All = new List<Info>
    {
        new("Other", "Khác", "📦"),
        new("Food", "Ăn uống", "🍜"),
        new("Transport", "Di chuyển", "🚗"),
        new("Accommodation", "Lưu trú", "🏨"),
        new("Entertainment", "Giải trí", "🎉"),
        new("Shopping", "Mua sắm", "🛍️"),
    };

    public static Info Get(string? category) =>
        All.FirstOrDefault(c => string.Equals(c.Value, category, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
