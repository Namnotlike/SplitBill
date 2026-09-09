using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SplitBill.Application.Common;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Auth;

/// <summary>Cài đặt CLAUDE.md mục 25.9 bằng AES-256-GCM — thuần BCL (`System.Security.Cryptography.
/// AesGcm`), KHÔNG cần NuGet package nào (đã verify trước khi triển khai — xem ghi chú trong CLAUDE.md
/// mục 25.9). Khóa AES-256 lấy từ SHA-256 của <see cref="TwoFactorOptions.EncryptionKey"/> (chuỗi bất
/// kỳ độ dài nào -> luôn ra đúng 32 byte). Định dạng lưu: Base64(nonce 12 byte || ciphertext ||
/// tag 16 byte).</summary>
public sealed class TwoFactorSecretProtector : ITwoFactorSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly TwoFactorOptions _options;

    public TwoFactorSecretProtector(IOptions<TwoFactorOptions> options)
    {
        _options = options.Value;
    }

    public string Protect(string plaintext)
    {
        var key = DeriveKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);
        return Convert.ToBase64String(result);
    }

    public string Unprotect(string protectedText)
    {
        var key = DeriveKey();
        var data = Convert.FromBase64String(protectedText);
        var nonce = data[..NonceSize];
        var tag = data[^TagSize..];
        var ciphertext = data[NonceSize..^TagSize];
        var plaintextBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private byte[] DeriveKey()
    {
        if (string.IsNullOrWhiteSpace(_options.EncryptionKey))
        {
            throw new DomainException(ErrorCodes.TwoFactorNotConfigured, "Xác thực 2 lớp chưa được cấu hình trên máy chủ.");
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(_options.EncryptionKey));
    }
}
