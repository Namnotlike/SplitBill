using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SplitBill.Application.Auth;

namespace SplitBill.Web.Services;

/// <summary>
/// Gắn Bearer access token (lưu trong cookie đăng nhập) vào mọi request gọi SplitBill.Api.
/// Nếu access token sắp hết hạn thì tự refresh trước, cập nhật lại cookie (mẫu BFF — trình duyệt
/// không bao giờ thấy JWT thật).
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public BearerTokenHandler(IHttpContextAccessor httpContextAccessor, IHttpClientFactory httpClientFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;

        if (user?.Identity?.IsAuthenticated == true)
        {
            var accessToken = user.FindFirst(SplitBillClaimTypes.AccessToken)?.Value;
            var expiresRaw = user.FindFirst(SplitBillClaimTypes.AccessTokenExpires)?.Value;
            var refreshToken = user.FindFirst(SplitBillClaimTypes.RefreshToken)?.Value;

            if (accessToken is not null && expiresRaw is not null
                && DateTimeOffset.TryParse(expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
                && expiresAt <= DateTimeOffset.UtcNow.AddSeconds(30)
                && refreshToken is not null)
            {
                var refreshed = await TryRefreshAsync(refreshToken, cancellationToken);
                if (refreshed is not null)
                {
                    accessToken = refreshed.AccessToken;
                    await ReSignInAsync(httpContext!, user, refreshed);
                }
            }

            if (accessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<AuthTokens?> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            // Dùng client "ApiRaw" (không gắn handler này) để tránh đệ quy vô hạn.
            var rawClient = _httpClientFactory.CreateClient("ApiRaw");
            var response = await rawClient.PostAsJsonAsync("auth/refresh", new { refreshToken }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task ReSignInAsync(HttpContext httpContext, ClaimsPrincipal currentUser, AuthTokens tokens)
    {
        var identity = new ClaimsIdentity(currentUser.Identity as ClaimsIdentity);
        SetTokenClaim(identity, SplitBillClaimTypes.AccessToken, tokens.AccessToken);
        SetTokenClaim(identity, SplitBillClaimTypes.AccessTokenExpires, tokens.AccessTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture));
        SetTokenClaim(identity, SplitBillClaimTypes.RefreshToken, tokens.RefreshToken);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    private static void SetTokenClaim(ClaimsIdentity identity, string type, string value)
    {
        var existing = identity.FindFirst(type);
        if (existing is not null)
        {
            identity.RemoveClaim(existing);
        }

        identity.AddClaim(new Claim(type, value));
    }
}
