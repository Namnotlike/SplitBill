namespace SplitBill.Web;

/// <summary>Class đánh dấu (marker) rỗng — chỉ dùng làm "địa chỉ" cho <c>IStringLocalizer&lt;SharedResource&gt;</c>
/// để tìm đúng file tài nguyên dùng chung <c>Resources/SharedResource.{culture}.resx</c> (CLAUDE.md
/// mục 22 — Đa ngôn ngữ). Dùng cho chuỗi xuất hiện ở NHIỀU trang (navbar, footer trong _Layout.cshtml)
/// — chuỗi riêng của 1 trang cụ thể dùng <c>IViewLocalizer</c> (tự động khớp theo đường dẫn trang) thay
/// vì class này.</summary>
public sealed class SharedResource
{
}
