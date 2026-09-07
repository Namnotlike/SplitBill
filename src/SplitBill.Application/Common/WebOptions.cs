namespace SplitBill.Application.Common;

/// <summary>
/// Origin của SplitBill.Web (BFF), dùng để dựng LINK TUYỆT ĐỐI trong email gửi ra ngoài (đặt lại mật
/// khẩu, thông báo — CLAUDE.md mục 13/16). Cấu hình qua <c>Web:BaseUrl</c> trong appsettings, ví dụ
/// <c>"http://localhost:5103"</c> (khớp cổng mặc định của SplitBill.Web — xem mục 10b).
///
/// ⚠️ Bug thật phát hiện khi làm tính năng "Quên mật khẩu" (2026-09-07): <see cref="Notifications.NotificationService"/>
/// từ trước tới nay luôn gửi <c>LinkUrl</c> dạng ĐƯỜNG DẪN TƯƠNG ĐỐI (vd "/Expenses/Index/{groupId}")
/// thẳng vào thẻ &lt;a href&gt; của email — một URL tương đối không có nghĩa khi mở từ 1 email client
/// (không có "trang hiện tại" nào để tính tương đối theo), nên MỌI link "Xem chi tiết" trong MỌI email
/// thông báo (khoản chi mới, settlement, nhắc nợ...) từ trước tới nay đều là link hỏng/không bấm được
/// gì hữu ích khi mở trực tiếp từ ứng dụng email — chỉ tình cờ không bị phát hiện vì thiếu 1 endpoint
/// gửi email thật sự CẦN người dùng bấm link để hoàn tất 1 hành động (đặt lại mật khẩu là ca đầu tiên
/// bắt buộc phải có link hoạt động được). Đã sửa: <c>NotificationService</c> giờ ghép
/// <see cref="BaseUrl"/> + đường dẫn tương đối thành URL tuyệt đối CHỈ khi dựng nội dung email — cột
/// <c>Notification.LinkUrl</c> lưu trong DB (dùng cho trang /Notifications same-origin) vẫn giữ
/// nguyên dạng tương đối, không đổi.
/// </summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    public string BaseUrl { get; set; } = "http://localhost:5103";
}
