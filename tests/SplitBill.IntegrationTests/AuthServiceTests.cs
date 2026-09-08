using System.Text.RegularExpressions;
using FluentAssertions;
using SplitBill.Application.Auth;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class AuthServiceTests
{
    /// <summary>Lấy token plaintext từ link "Đặt lại mật khẩu" trong email vừa gửi — token thật KHÔNG
    /// BAO GIỜ có trong DB (chỉ có hash), nên test phải "đọc trộm" từ email giống người dùng thật.</summary>
    private static string ExtractResetTokenFromEmail(string htmlBody)
    {
        var match = Regex.Match(htmlBody, @"token=([^""&]+)");
        match.Success.Should().BeTrue("email đặt lại mật khẩu phải chứa link kèm token");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    [Fact]
    public async Task Register_ThenLogin_Succeeds()
    {
        using var harness = TestHarness.Create();

        await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "A"), CancellationToken.None);
        var tokens = await harness.AuthService.LoginAsync(new LoginRequest("a@example.com", "Passw0rd123"), CancellationToken.None);

        tokens.AccessToken.Should().NotBeNullOrEmpty();
        tokens.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_DuplicateEmail_ThrowsEmailAlreadyRegistered()
    {
        using var harness = TestHarness.Create();
        await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "A"), CancellationToken.None);

        var act = () => harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Other12345", "A2"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.EmailAlreadyRegistered);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsInvalidCredentials()
    {
        using var harness = TestHarness.Create();
        await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "A"), CancellationToken.None);

        var act = () => harness.AuthService.LoginAsync(new LoginRequest("a@example.com", "WrongPassword"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_UnknownEmail_ThrowsInvalidCredentials_NotLeakingExistence()
    {
        using var harness = TestHarness.Create();

        var act = () => harness.AuthService.LoginAsync(new LoginRequest("nobody@example.com", "Passw0rd123"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Refresh_ValidToken_IssuesNewTokens_AndRevokesOld()
    {
        using var harness = TestHarness.Create();
        var initial = await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "A"), CancellationToken.None);

        var refreshed = await harness.AuthService.RefreshAsync(initial.RefreshToken, CancellationToken.None);

        refreshed.AccessToken.Should().NotBe(initial.AccessToken);
        refreshed.RefreshToken.Should().NotBe(initial.RefreshToken);

        // Token cũ đã bị thu hồi (rotation) — dùng lại phải lỗi.
        var act = () => harness.AuthService.RefreshAsync(initial.RefreshToken, CancellationToken.None);
        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        using var harness = TestHarness.Create();
        var tokens = await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "A"), CancellationToken.None);

        await harness.AuthService.LogoutAsync(tokens.RefreshToken, CancellationToken.None);

        var act = () => harness.AuthService.RefreshAsync(tokens.RefreshToken, CancellationToken.None);
        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidRefreshToken);
    }

    // ===== Quên mật khẩu (CLAUDE.md mục 16, bổ sung 2026-09-07) =====

    [Fact]
    public async Task ForgotPasswordAsync_KnownEmail_SendsEmailWithAbsoluteResetLink()
    {
        using var harness = TestHarness.Create();
        await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "Nam"), CancellationToken.None);

        await harness.AuthService.ForgotPasswordAsync("a@example.com", CancellationToken.None);

        var sent = harness.EmailSender.SentEmails.Should().ContainSingle(e => e.ToEmail == "a@example.com").Which;
        // Link PHẢI tuyệt đối (có scheme+host) — link tương đối không mở được từ email client (bug đã
        // sửa cùng đợt, xem WebOptions).
        sent.HtmlBody.Should().Contain("http://localhost:5103/Account/ResetPassword?token=");
    }

    [Fact]
    public async Task ForgotPasswordAsync_UnknownEmail_DoesNotThrow_NoEmailSent()
    {
        using var harness = TestHarness.Create();

        var act = () => harness.AuthService.ForgotPasswordAsync("nobody@example.com", CancellationToken.None);

        await act.Should().NotThrowAsync(); // không tiết lộ email có tồn tại hay không (mục 8)
        harness.EmailSender.SentEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task ForgotPasswordAsync_UnknownEmail_TakesAtLeastAsLongAsKnownEmail_NoTimingSideChannel()
    {
        // Phát hiện qua security-review (2026-09-08): nhánh "email không tồn tại" trước đây trả về
        // gần như tức thì (chỉ 1 SELECT), trong khi nhánh "email tồn tại" tốn thêm thời gian ghi DB +
        // gửi email — chênh lệch độ trễ này là 1 kênh rò rỉ (timing side-channel) cho phép dò email
        // nào đã đăng ký mà không cần đọc response body. Đã sửa bằng sàn thời gian tối thiểu chung cho
        // cả 2 nhánh (AuthService.ForgotPasswordMinDuration). Test này xác nhận nhánh "không tồn tại"
        // (vốn nhanh nhất) vẫn bị giữ lại đủ lâu, không trả về ngay lập tức.
        using var harness = TestHarness.Create();
        await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "Nam"), CancellationToken.None);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await harness.AuthService.ForgotPasswordAsync("nobody@example.com", CancellationToken.None);
        sw.Stop();

        // Sàn thực tế là 500ms — cho phép sai số nhỏ (đồng hồ/lịch trình luồng) bằng cách chỉ đòi hỏi
        // tối thiểu 400ms, tránh test flaky trên máy CI chậm mà vẫn đủ chứng minh KHÔNG trả về tức thì.
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(400);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_ChangesPassword_AndRevokesAllRefreshTokens()
    {
        using var harness = TestHarness.Create();
        var initial = await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "Nam"), CancellationToken.None);
        await harness.AuthService.ForgotPasswordAsync("a@example.com", CancellationToken.None);
        var token = ExtractResetTokenFromEmail(harness.EmailSender.SentEmails.Single().HtmlBody);

        await harness.AuthService.ResetPasswordAsync(new ResetPasswordRequest(token, "NewPassw0rd456"), CancellationToken.None);

        // Mật khẩu cũ không dùng được nữa, mật khẩu mới đăng nhập được.
        var oldLogin = () => harness.AuthService.LoginAsync(new LoginRequest("a@example.com", "Passw0rd123"), CancellationToken.None);
        (await oldLogin.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidCredentials);
        var newLogin = await harness.AuthService.LoginAsync(new LoginRequest("a@example.com", "NewPassw0rd456"), CancellationToken.None);
        newLogin.AccessToken.Should().NotBeNullOrEmpty();

        // RefreshToken phát hành TRƯỚC lúc đổi mật khẩu phải bị thu hồi (đăng xuất mọi phiên khác).
        var refreshOld = () => harness.AuthService.RefreshAsync(initial.RefreshToken, CancellationToken.None);
        (await refreshOld.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task ResetPasswordAsync_TokenAlreadyUsed_ThrowsInvalidResetToken()
    {
        using var harness = TestHarness.Create();
        await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "Nam"), CancellationToken.None);
        await harness.AuthService.ForgotPasswordAsync("a@example.com", CancellationToken.None);
        var token = ExtractResetTokenFromEmail(harness.EmailSender.SentEmails.Single().HtmlBody);
        await harness.AuthService.ResetPasswordAsync(new ResetPasswordRequest(token, "NewPassw0rd456"), CancellationToken.None);

        // Dùng lại đúng token đó lần 2 — dù về lý thuyết vẫn còn hạn, đã UsedAt nên không dùng lại được.
        var act = () => harness.AuthService.ResetPasswordAsync(new ResetPasswordRequest(token, "AnotherPassw0rd"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidResetToken);
    }

    [Fact]
    public async Task ResetPasswordAsync_UnknownToken_ThrowsInvalidResetToken()
    {
        using var harness = TestHarness.Create();

        var act = () => harness.AuthService.ResetPasswordAsync(new ResetPasswordRequest("not-a-real-token", "Passw0rd123"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidResetToken);
    }

    [Fact]
    public async Task ResetPasswordAsync_ExpiredToken_ThrowsInvalidResetToken()
    {
        using var harness = TestHarness.Create();
        await harness.AuthService.RegisterAsync(new RegisterRequest("a@example.com", "Passw0rd123", "Nam"), CancellationToken.None);
        await harness.AuthService.ForgotPasswordAsync("a@example.com", CancellationToken.None);
        var token = ExtractResetTokenFromEmail(harness.EmailSender.SentEmails.Single().HtmlBody);
        // Mô phỏng "đã hết hạn" mà không phải chờ thời gian thật trôi qua — đẩy lùi ExpiresAt trực
        // tiếp trên DbContext (test harness có quyền truy cập thẳng, giống mẫu DebtReminderTests).
        var storedToken = harness.DbContext.Set<PasswordResetToken>().Single();
        storedToken.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await harness.DbContext.SaveChangesAsync(CancellationToken.None);

        var act = () => harness.AuthService.ResetPasswordAsync(new ResetPasswordRequest(token, "Passw0rd123"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidResetToken);
    }
}
