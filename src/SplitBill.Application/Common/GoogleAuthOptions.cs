namespace SplitBill.Application.Common;

/// <summary>
/// Bí mật dùng chung giữa <c>SplitBill.Web</c> và <c>SplitBill.Api</c> để bảo vệ endpoint
/// <c>POST /auth/google</c> (CLAUDE.md mục 25.3, hạng mục "Đăng nhập bằng Google").
///
/// Endpoint đó nhận thẳng <c>{ GoogleId, Email, DisplayName }</c> từ Web (không phải ID token đã ký
/// của Google) — vì Web đã tự xác thực toàn bộ luồng OAuth với Google (dùng ClientSecret, qua
/// <c>Microsoft.AspNetCore.Authentication.Google</c>) TRƯỚC khi gọi Api, nên các claim đó đáng tin
/// CHỈ KHI request thực sự đến từ Web. Nếu Api tin thẳng body mà không kiểm tra gì, bất kỳ ai cũng gọi
/// thẳng được endpoint này với 1 email tùy ý để chiếm tài khoản người khác — vì vậy Web phải đính kèm
/// header <c>X-Internal-Secret</c> khớp đúng giá trị này, và Api từ chối (401 <c>INVALID_INTERNAL_SECRET</c>)
/// nếu thiếu/sai/chưa cấu hình. Cùng cơ chế "user tự đặt qua user-secrets, KHÔNG có giá trị mặc định
/// an toàn" như <c>JwtOptions.SigningKey</c> — nhưng KHÔNG fail-fast lúc khởi động (khác SigningKey):
/// Google OAuth là tính năng tùy chọn, để trống chỉ khiến riêng endpoint này luôn từ chối (fail
/// closed), không chặn toàn bộ ứng dụng chạy khi người dùng chưa kịp thiết lập Google Cloud OAuth
/// client.
/// </summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string InternalSecret { get; set; } = string.Empty;
}
