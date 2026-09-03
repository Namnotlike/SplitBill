namespace SplitBill.Domain.Enums;

/// <summary>Quy tắc chia tiền cho một khoản chi. Xem CLAUDE.md mục 5.1.</summary>
public enum SplitMode
{
    /// <summary>Chia đều cho danh sách thành viên.</summary>
    Equal = 0,

    /// <summary>Chia theo trọng số (phần): 1, 1, 2, 0.5...</summary>
    Shares = 1,

    /// <summary>Chia theo phần trăm.</summary>
    Percentage = 2,

    /// <summary>Nhập tay số tiền từng người.</summary>
    ExactAmount = 3,

    /// <summary>Gán theo món ăn/hạng mục.</summary>
    Itemized = 4,
}
