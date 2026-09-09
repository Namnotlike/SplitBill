using System.Security.Cryptography;
using System.Text;

namespace SplitBill.Application.Auth;

/// <summary>Cài đặt CLAUDE.md mục 25.9 bằng <see cref="HMACSHA1"/> thuần BCL — KHÔNG cần NuGet package
/// nào. Đã verify TRƯỚC KHI triển khai bằng cách chạy thử đúng 5 test vector chính thức của RFC 6238
/// Phụ lục B (HMAC-SHA1, secret ASCII "12345678901234567890") qua 1 script console throwaway, cả 5
/// đều khớp — xem CLAUDE.md mục 25.9. Dùng 6 chữ số (KHÁC 8 chữ số của chính test vector RFC — RFC chỉ
/// dùng 8 số để ví dụ không mơ hồ, còn MỌI app xác thực thật (Google/Microsoft Authenticator, Authy)
/// đều mặc định 6 số và sẽ hiển thị sai nếu server tính 8 số).</summary>
public sealed class TotpService : ITotpService
{
    private const int Digits = 6;
    private const int PeriodSeconds = 30;
    private const string Issuer = "SplitBill";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20); // 160-bit, khuyến nghị RFC 4226 §4
        return Base32Encode(bytes);
    }

    public string BuildOtpAuthUri(string secretBase32, string accountLabel)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{accountLabel}");
        var issuer = Uri.EscapeDataString(Issuer);
        // digits=6 tường minh — otpauth:// URI không có tham số này thì app xác thực mặc định 6 số,
        // nhưng ghi rõ để tránh mọi hiểu lầm/khác biệt cài đặt giữa các app.
        return $"otpauth://totp/{label}?secret={secretBase32}&issuer={issuer}&digits={Digits}&period={PeriodSeconds}";
    }

    public long? TryMatchTimeStep(string secretBase32, string code, DateTimeOffset now, int windowSteps = 1)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var key = Base32Decode(secretBase32);
        var currentStep = now.ToUnixTimeSeconds() / PeriodSeconds;

        for (var delta = -windowSteps; delta <= windowSteps; delta++)
        {
            var step = currentStep + delta;
            if (Hotp(key, step) == code)
            {
                return step;
            }
        }

        return null;
    }

    private static string Hotp(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binCode = ((hash[offset] & 0x7f) << 24)
                    | ((hash[offset + 1] & 0xff) << 16)
                    | ((hash[offset + 2] & 0xff) << 8)
                    | (hash[offset + 3] & 0xff);
        var otp = binCode % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int bits = 0, value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Base32Alphabet[(value << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    private static byte[] Base32Decode(string s)
    {
        var bytes = new List<byte>();
        int bits = 0, value = 0;
        foreach (var c in s)
        {
            var idx = Base32Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (idx < 0)
            {
                continue; // bỏ qua ký tự đệm/không hợp lệ (vd người dùng gõ tay có khoảng trắng)
            }

            value = (value << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}
