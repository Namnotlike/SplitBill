namespace SplitBill.Domain.Exceptions;

/// <summary>
/// Ngoại lệ nghiệp vụ dùng chung. Tầng API bắt exception này và map sang
/// <c>ProblemDetails</c> kèm <see cref="ErrorCode"/> (xem CLAUDE.md mục 8).
/// </summary>
public class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
