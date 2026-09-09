using System.Security.Cryptography;
using FluentAssertions;
using SplitBill.Application.Auth;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>CLAUDE.md mục 25.9 — Xác thực 2 lớp / TOTP. Sinh mã "hiện tại" bằng đúng thuật toán RFC
/// 6238 (đã verify khớp test vector chính thức trước khi triển khai, xem CLAUDE.md mục 25.9) — trùng
/// lặp có chủ đích với <c>TotpService</c> sản phẩm, vì đây là công cụ TEST (đóng vai "app xác thực"
/// của người dùng), không phải logic cần tái sử dụng.</summary>
public sealed class TwoFactorServiceTests
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string CurrentCode(string secretBase32)
    {
        var key = Base32Decode(secretBase32);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        return Hotp(key, counter);
    }

    private static string Hotp(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binCode = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return (binCode % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string s)
    {
        var bytes = new List<byte>();
        int bits = 0, value = 0;
        foreach (var c in s)
        {
            var idx = Base32Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (idx < 0) continue;
            value = (value << 5) | idx;
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)((value >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return bytes.ToArray();
    }

    [Fact]
    public async Task SetupAsync_ThenEnableWithValidCode_TurnsOnAndReturns10RecoveryCodes()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var setup = await harness.TwoFactorService.SetupAsync(userId, CancellationToken.None);
        var code = CurrentCode(setup.SecretBase32);
        var result = await harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest(code), CancellationToken.None);

        result.RecoveryCodes.Should().HaveCount(10);
        result.RecoveryCodes.Should().OnlyHaveUniqueItems();
        (await harness.TwoFactorService.GetStatusAsync(userId, CancellationToken.None)).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnableAsync_WrongCode_ThrowsInvalidTwoFactorCode()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        await harness.TwoFactorService.SetupAsync(userId, CancellationToken.None);

        var act = () => harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest("000000"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidTwoFactorCode);
    }

    [Fact]
    public async Task EnableAsync_WithoutSetupFirst_ThrowsTwoFactorSetupRequired()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var act = () => harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest("123456"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.TwoFactorSetupRequired);
    }

    [Fact]
    public async Task DisableAsync_WithValidRecoveryCode_TurnsOffAndClearsRecoveryCodes()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var setup = await harness.TwoFactorService.SetupAsync(userId, CancellationToken.None);
        var enableResult = await harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest(CurrentCode(setup.SecretBase32)), CancellationToken.None);
        var recoveryCode = enableResult.RecoveryCodes[0];

        await harness.TwoFactorService.DisableAsync(userId, new DisableTwoFactorRequest(recoveryCode), CancellationToken.None);

        (await harness.TwoFactorService.GetStatusAsync(userId, CancellationToken.None)).Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task RecoveryCode_CanOnlyBeUsedOnce()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var setup = await harness.TwoFactorService.SetupAsync(userId, CancellationToken.None);
        var enableResult = await harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest(CurrentCode(setup.SecretBase32)), CancellationToken.None);
        var recoveryCode = enableResult.RecoveryCodes[0];

        // Dùng mã dự phòng lần 1 qua VerifyCodeOrRecoveryAsync (giả lập luồng hoàn tất đăng nhập) — thành công.
        var user = harness.DbContext.Set<Domain.Entities.User>().Single(u => u.Id == userId);
        (await harness.TwoFactorService.VerifyCodeOrRecoveryAsync(user, recoveryCode, CancellationToken.None)).Should().BeTrue();

        // Dùng lại đúng mã đó lần 2 -> phải thất bại (đã tiêu thụ).
        (await harness.TwoFactorService.VerifyCodeOrRecoveryAsync(user, recoveryCode, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task RegenerateRecoveryCodesAsync_InvalidatesOldCodes()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var setup = await harness.TwoFactorService.SetupAsync(userId, CancellationToken.None);
        var enableResult = await harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest(CurrentCode(setup.SecretBase32)), CancellationToken.None);
        var oldCode = enableResult.RecoveryCodes[0];

        // Cần 1 mã TOTP mới, khác time step đã dùng lúc Enable (chống replay) — chờ tự nhiên không khả
        // thi trong test, nên chỉ verify bằng đúng luồng: EnableAsync đã tiêu thụ time step hiện tại,
        // Regenerate cần đợi bước kế tiếp. Thay vào đó test bằng cách gọi lại SetupAsync (tạo secret
        // mới, TwoFactorLastUsedTimeStep bị reset về null) rồi Enable lại — mô phỏng gián tiếp cùng bất
        // biến "mã dự phòng cũ bị vô hiệu" mà không phụ thuộc thời gian thực.
        var user = harness.DbContext.Set<Domain.Entities.User>().Single(u => u.Id == userId);
        user.TwoFactorLastUsedTimeStep = null; // mô phỏng đã sang time step mới
        await harness.DbContext.SaveChangesAsync(CancellationToken.None);

        var regenerateResult = await harness.TwoFactorService.RegenerateRecoveryCodesAsync(
            userId, new RegenerateRecoveryCodesRequest(CurrentCode(setup.SecretBase32)), CancellationToken.None);

        regenerateResult.RecoveryCodes.Should().NotContain(oldCode);
        (await harness.TwoFactorService.VerifyCodeOrRecoveryAsync(user, oldCode, CancellationToken.None)).Should().BeFalse();
        (await harness.TwoFactorService.VerifyCodeOrRecoveryAsync(user, regenerateResult.RecoveryCodes[0], CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task DisableAsync_NotEnabled_ThrowsTwoFactorNotEnabled()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var act = () => harness.TwoFactorService.DisableAsync(userId, new DisableTwoFactorRequest("123456"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.TwoFactorNotEnabled);
    }

    /// <summary>CLAUDE.md mục 25.9, phần "Giới hạn đã biết" — mô phỏng `TwoFactor:EncryptionKey` bị
    /// đổi SAU KHI user đã bật 2FA bằng cách làm hỏng trực tiếp `TwoFactorSecretEncrypted` đã lưu
    /// (đổi 1 ký tự Base64 → AES-GCM tag mismatch, cùng đúng loại lỗi `CryptographicException` mà
    /// đổi khóa thật sẽ gây ra). `EnableAsync` KHÔNG có mã dự phòng nào để dùng thay thế (mã dự phòng
    /// chỉ được cấp SAU khi Enable thành công) nên phải báo lỗi rõ ràng thay vì sập 500 chung chung.</summary>
    [Fact]
    public async Task EnableAsync_SecretDecryptionFails_ThrowsTwoFactorDecryptionFailed()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        await harness.TwoFactorService.SetupAsync(userId, CancellationToken.None);
        CorruptStoredSecret(harness, userId);

        var act = () => harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest("123456"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.TwoFactorDecryptionFailed);
    }

    /// <summary>Khác `EnableAsync` ở test trên — `VerifyCodeOrRecoveryAsync` (dùng bởi cả DisableAsync
    /// lẫn luồng hoàn tất đăng nhập 2 bước) CÓ đường dự phòng thật (mã dự phòng, không phụ thuộc
    /// secret TOTP). Giải mã lỗi phải rơi êm xuống nhánh mã dự phòng thay vì ném lỗi chặn đứng luôn cả
    /// đường thoát hiểm này — đây chính là bug advisor phát hiện trước khi commit, đã sửa.</summary>
    [Fact]
    public async Task VerifyCodeOrRecoveryAsync_SecretDecryptionFails_FallsBackToRecoveryCode()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var setup = await harness.TwoFactorService.SetupAsync(userId, CancellationToken.None);
        var enableResult = await harness.TwoFactorService.EnableAsync(userId, new EnableTwoFactorRequest(CurrentCode(setup.SecretBase32)), CancellationToken.None);
        var recoveryCode = enableResult.RecoveryCodes[0];
        CorruptStoredSecret(harness, userId);

        var user = harness.DbContext.Set<Domain.Entities.User>().Single(u => u.Id == userId);

        // Mã TOTP sai (không phải mã dự phòng) -> false êm ả, KHÔNG ném lỗi hạ tầng ra ngoài.
        (await harness.TwoFactorService.VerifyCodeOrRecoveryAsync(user, "000000", CancellationToken.None)).Should().BeFalse();
        // Mã dự phòng hợp lệ -> vẫn xác thực được dù secret TOTP không giải mã được.
        (await harness.TwoFactorService.VerifyCodeOrRecoveryAsync(user, recoveryCode, CancellationToken.None)).Should().BeTrue();
    }

    private static void CorruptStoredSecret(TestHarness harness, Guid userId)
    {
        var user = harness.DbContext.Set<Domain.Entities.User>().Single(u => u.Id == userId);
        var chars = user.TwoFactorSecretEncrypted!.ToCharArray();
        chars[0] = chars[0] == 'A' ? 'B' : 'A'; // vẫn là Base64 hợp lệ, nhưng nội dung/tag đã sai
        user.TwoFactorSecretEncrypted = new string(chars);
        harness.DbContext.SaveChanges();
    }
}
