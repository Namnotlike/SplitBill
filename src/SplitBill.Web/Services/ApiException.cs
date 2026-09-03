namespace SplitBill.Web.Services;

/// <summary>Bọc lỗi ProblemDetails trả về từ SplitBill.Api (CLAUDE.md mục 8) để các trang Razor hiển thị.</summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public ApiException(int statusCode, string errorCode, string message) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
