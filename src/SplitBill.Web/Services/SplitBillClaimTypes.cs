namespace SplitBill.Web.Services;

/// <summary>Tên claim tùy chỉnh lưu token trong cookie đăng nhập (mã hóa bởi ASP.NET Core Data Protection).</summary>
public static class SplitBillClaimTypes
{
    public const string AccessToken = "splitbill:access_token";
    public const string AccessTokenExpires = "splitbill:access_token_expires";
    public const string RefreshToken = "splitbill:refresh_token";
}
