using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SplitBill.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Lấy UserId từ claim "sub" của JWT access token hiện tại.</summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Token không có claim 'sub'.");
        return Guid.Parse(sub);
    }
}
