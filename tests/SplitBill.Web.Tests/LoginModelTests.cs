using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Web.Pages.Account;
using SplitBill.Web.Services;
using Xunit;

namespace SplitBill.Web.Tests;

public sealed class LoginModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static LoginModel CreateModel(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/v1/") };
        var apiClient = new SplitBillApiClient(httpClient, WebTestHelpers.EmptyConfiguration());

        var model = new LoginModel(apiClient, WebTestHelpers.EmptyConfiguration());
        var httpContext = WebTestHelpers.CreateHttpContext();
        WebTestHelpers.AttachPageContext(model, httpContext);
        return model;
    }

    [Fact]
    public async Task OnPostAsync_ValidCredentials_SignsInAndRedirectsToGroups()
    {
        var model = CreateModel(req =>
        {
            object body = req.RequestUri!.AbsolutePath.EndsWith("users/me")
                ? new { id = Guid.NewGuid(), email = "a@example.com", displayName = "Nam", bankAccountNumber = (string?)null, bankBin = (string?)null }
                : new
                {
                    requiresTwoFactor = false,
                    tokens = new { accessToken = "at", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), refreshToken = "rt", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(14) },
                    twoFactorChallengeToken = (string?)null,
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
            };
        });
        model.Input = new LoginModel.InputModel { Email = "a@example.com", Password = "Passw0rd123" };

        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<LocalRedirectResult>();
        ((LocalRedirectResult)result).Url.Should().Be("/Groups/Index");
        model.HttpContext.User.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task OnPostAsync_ProfileFetchReturnsMalformedBody_StillSignsInWithFallbackName()
    {
        // Trước đây: nếu /users/me trả 200 nhưng body méo mó (DisplayName null), toàn bộ đăng nhập
        // sập với ArgumentNullException không được bắt (catch chỉ bắt ApiException). Đăng nhập PHẢI
        // vẫn thành công với tên fallback — xem SignInHelper.SignInAsync.
        var model = CreateModel(req =>
        {
            object body = req.RequestUri!.AbsolutePath.EndsWith("users/me")
                ? new { } // thiếu hết field -> DisplayName sẽ null lúc deserialize
                : new
                {
                    requiresTwoFactor = false,
                    tokens = new { accessToken = "at", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), refreshToken = "rt", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(14) },
                    twoFactorChallengeToken = (string?)null,
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
            };
        });
        model.Input = new LoginModel.InputModel { Email = "a@example.com", Password = "Passw0rd123" };

        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<LocalRedirectResult>();
        model.HttpContext.User.Identity!.IsAuthenticated.Should().BeTrue();
        model.HttpContext.User.Identity.Name.Should().Be("a@example.com"); // tên fallback = email đã nhập
    }

    [Fact]
    public async Task OnPostAsync_RequiresTwoFactor_RedirectsToChallengePage_DoesNotSignIn()
    {
        // CLAUDE.md mục 25.9 — LoginAsync trả RequiresTwoFactor=true (không có Tokens) khi tài khoản
        // đã bật 2FA; LoginModel KHÔNG được gọi SignInHelper ở bước này (chưa đủ điều kiện đăng nhập).
        var model = CreateModel(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { requiresTwoFactor = true, tokens = (object?)null, twoFactorChallengeToken = "challenge-abc" }, JsonOptions),
                Encoding.UTF8, "application/json"),
        });
        model.Input = new LoginModel.InputModel { Email = "a@example.com", Password = "Passw0rd123" };

        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<RedirectToPageResult>();
        ((RedirectToPageResult)result).PageName.Should().Be("/Account/TwoFactorChallenge");
        model.HttpContext.User.Identity!.IsAuthenticated.Should().BeFalse();
        model.TempData["TwoFactorChallengeToken"].Should().Be("challenge-abc");
    }

    [Fact]
    public async Task OnPostAsync_WrongPassword_ShowsFriendlyErrorMessage()
    {
        var model = CreateModel(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"title":"Email hoặc mật khẩu không đúng.","errorCode":"INVALID_CREDENTIALS"}""",
                Encoding.UTF8, "application/problem+json"),
        });
        model.Input = new LoginModel.InputModel { Email = "a@example.com", Password = "wrong" };

        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Be("Email hoặc mật khẩu không đúng.");
        model.HttpContext.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task OnPostAsync_InvalidModelState_ReturnsPageWithoutCallingApi()
    {
        var called = false;
        var model = CreateModel(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        model.Input = new LoginModel.InputModel { Email = "", Password = "" };
        model.ModelState.AddModelError("Input.Email", "Required");

        var result = await model.OnPostAsync(CancellationToken.None);

        result.Should().BeOfType<PageResult>();
        called.Should().BeFalse();
    }
}
