using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SplitBill.Application.Auth;

namespace SplitBill.Web.Services;

/// <summary>Đăng nhập cookie session sau khi có AuthTokens từ /auth/register hoặc /auth/login.</summary>
public static class SignInHelper
{
    public static async Task SignInAsync(HttpContext httpContext, SplitBillApiClient apiClient, AuthTokens tokens, string fallbackName, CancellationToken cancellationToken)
    {
        // Bước 1: đăng nhập tạm với tên hiển thị fallback (email) để BearerTokenHandler có token
        // mà gọi /users/me lấy tên thật.
        var principal = BuildPrincipal(fallbackName, null, null, tokens);
        httpContext.User = principal;
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        try
        {
            var profile = await apiClient.GetMeAsync(cancellationToken);
            var finalPrincipal = BuildPrincipal(profile.DisplayName, profile.Id.ToString(), profile.Email, tokens);
            httpContext.User = finalPrincipal;
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, finalPrincipal);
        }
        catch (ApiException)
        {
            // Không chặn đăng nhập nếu /users/me tạm thời lỗi — vẫn còn tên fallback.
        }
    }

    private static ClaimsPrincipal BuildPrincipal(string name, string? userId, string? email, AuthTokens tokens)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new(SplitBillClaimTypes.AccessToken, tokens.AccessToken),
            new(SplitBillClaimTypes.AccessTokenExpires, tokens.AccessTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture)),
            new(SplitBillClaimTypes.RefreshToken, tokens.RefreshToken),
        };

        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }

        if (email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, null);
        return new ClaimsPrincipal(identity);
    }
}
