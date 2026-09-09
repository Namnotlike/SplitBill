namespace SplitBill.Domain.Entities;

/// <summary>Mẫu nhóm tái sử dụng (CLAUDE.md mục 25.6, bổ sung 2026-09-09) — lưu sẵn "khung" 1 nhóm
/// (Type/Currency/SimplifyDebts + danh sách tên khách vãng lai hay đi cùng) để tạo nhóm mới trong 1
/// lượt, không phải thêm lại từng thành viên mỗi lần. Khác "Nhân bản nhóm" (mục 18): mẫu này là tài
/// nguyên CỦA RIÊNG 1 User (không phải của 1 Group), không phụ thuộc vào bất kỳ Group cụ thể nào còn
/// tồn tại hay không — người dùng có thể xóa nhóm gốc mà mẫu vẫn dùng được bình thường.</summary>
public sealed class GroupTemplate
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = "VND";
    public bool SimplifyDebts { get; set; } = true;

    /// <summary>JSON <c>string[]</c> — tên hiển thị của các khách vãng lai sẽ được thêm tự động khi
    /// tạo nhóm từ mẫu này. Chỉ lưu tên (không lưu UserId) vì mẫu có thể dùng lại nhiều lần, không có
    /// gì đảm bảo cùng 1 tài khoản còn muốn tham gia ở mỗi lần dùng.</summary>
    public string MemberNamesJson { get; set; } = "[]";

    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
