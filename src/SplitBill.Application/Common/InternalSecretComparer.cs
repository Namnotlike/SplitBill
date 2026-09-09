using System.Security.Cryptography;
using System.Text;

namespace SplitBill.Application.Common;

/// <summary>
/// So sánh bí mật dùng chung (CLAUDE.md mục 25.3, xem <see cref="GoogleAuthOptions"/>) bằng thời gian
/// cố định (<see cref="CryptographicOperations.FixedTimeEquals"/>) — tránh kênh rò rỉ qua thời gian so
/// sánh chuỗi thông thường (`==`/`string.Equals` dừng sớm ngay ký tự đầu tiên khác nhau), cùng mức độ
/// cẩn trọng đã áp dụng cho <c>AuthService.ForgotPasswordMinDuration</c> (CLAUDE.md mục 16.2). Tách
/// thành class riêng, không phải logic private trong controller, để viết unit test độc lập được.
/// </summary>
public static class InternalSecretComparer
{
    public static bool Matches(string? provided, string? expected)
    {
        if (string.IsNullOrEmpty(expected))
        {
            // Chưa cấu hình bí mật (GoogleOAuth chưa được thiết lập) -> luôn từ chối (fail closed),
            // không bao giờ coi 1 header rỗng là khớp với 1 secret rỗng.
            return false;
        }

        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        if (providedBytes.Length != expectedBytes.Length)
        {
            // FixedTimeEquals ném lỗi nếu 2 mảng khác độ dài — độ dài secret cố định do người vận
            // hành tự đặt (không đoán được qua thời gian phản hồi), nên nhánh rẽ sớm này không tạo
            // thêm kênh rò rỉ thực tế nào.
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
