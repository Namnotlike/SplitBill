namespace SplitBill.Application.Auth;

/// <summary>Mã hóa/giải mã secret TOTP tại rest (CLAUDE.md mục 25.9) — KHÁC hoàn toàn PasswordHasher
/// (băm 1 chiều): secret TOTP bắt buộc phải giải mã lại được để tính mã HOTP mỗi lần đăng nhập, nên
/// đây là mã hóa 2 chiều (AES-GCM), không phải hash.</summary>
public interface ITwoFactorSecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedText);
}
