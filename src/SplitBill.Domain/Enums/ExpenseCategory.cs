namespace SplitBill.Domain.Enums;

/// <summary>Nhãn/danh mục khoản chi (CLAUDE.md mục 15.3, bổ sung 2026-09-05) — cho phép người dùng
/// gắn nhãn để sau này thống kê chi tiêu theo loại. Danh sách cố định (không cho tự đặt nhãn tùy ý)
/// để bảng thống kê sau này (M6+) có trục phân loại nhất quán, thay vì tràn lan nhãn tự do khó gộp
/// nhóm. <see cref="Other"/> = 0 để mọi `Expense` cũ (tạo trước khi có tính năng này) tự động rơi vào
/// đây sau migration, không cần backfill dữ liệu.</summary>
public enum ExpenseCategory
{
    Other = 0,
    Food = 1,
    Transport = 2,
    Accommodation = 3,
    Entertainment = 4,
    Shopping = 5,
}
