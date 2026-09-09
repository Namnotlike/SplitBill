using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Api.Middleware;

/// <summary>
/// Bắt exception nghiệp vụ (<see cref="DomainException"/>) và xung đột đồng thời
/// (<see cref="DbUpdateConcurrencyException"/>), map sang RFC 7807 ProblemDetails
/// kèm errorCode (CLAUDE.md mục 8).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, MapStatusCode(ex.ErrorCode), ex.ErrorCode, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                ErrorCodes.ConcurrencyConflict,
                "Dữ liệu đã bị người khác thay đổi. Vui lòng tải lại và thử lại.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi xử lý request {Path}", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_SERVER_ERROR",
                "Đã có lỗi xảy ra ở máy chủ.");
        }
    }

    private static int MapStatusCode(string errorCode) => errorCode switch
    {
        ErrorCodes.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ErrorCodes.InvalidRefreshToken => StatusCodes.Status401Unauthorized,
        ErrorCodes.InvalidInternalSecret => StatusCodes.Status401Unauthorized,
        ErrorCodes.InvalidTwoFactorCode => StatusCodes.Status401Unauthorized,
        ErrorCodes.InvalidTwoFactorChallenge => StatusCodes.Status401Unauthorized,
        ErrorCodes.InsufficientRole => StatusCodes.Status403Forbidden,
        ErrorCodes.MemberNotInGroup => StatusCodes.Status403Forbidden,
        ErrorCodes.GroupNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.ExpenseNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.MemberNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.NotificationNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.EmailAlreadyRegistered => StatusCodes.Status409Conflict,
        ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ErrorCodes.MemberHasOutstandingBalance => StatusCodes.Status409Conflict,
        ErrorCodes.LastOwnerCannotBeRemoved => StatusCodes.Status409Conflict,
        ErrorCodes.SettlementNotPending => StatusCodes.Status409Conflict,
        // Không phải lỗi người dùng có thể tự sửa bằng cách thử lại — luôn do cấu hình máy chủ
        // (TwoFactor:EncryptionKey đã đổi sau khi user bật 2FA, xem CLAUDE.md mục 25.9), nên 500 thay
        // vì 400, nhưng vẫn có errorCode riêng để phân biệt với lỗi máy chủ chung chung.
        ErrorCodes.TwoFactorDecryptionFailed => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest,
    };

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string errorCode, string message)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = message,
            Type = $"https://splitbill/errors/{errorCode}",
        };
        problemDetails.Extensions["errorCode"] = errorCode;

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(problemDetails);
    }
}
