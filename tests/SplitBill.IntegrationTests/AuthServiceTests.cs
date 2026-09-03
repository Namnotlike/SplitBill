using FluentAssertions;
using SplitBill.Application.Auth;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class AuthServiceTests
{
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
}
