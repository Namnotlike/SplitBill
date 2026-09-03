using FluentValidation;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Common;

/// <summary>
/// Chạy FluentValidation và ném <see cref="DomainException"/> (errorCode VALIDATION_FAILED) thay vì
/// FluentValidation.ValidationException, để tầng Api map thống nhất về ProblemDetails (mục 8).
/// </summary>
public static class ValidatorExtensions
{
    public static void ValidateOrThrowDomainException<T>(this IValidator<T> validator, T instance)
    {
        var result = validator.Validate(instance);
        if (!result.IsValid)
        {
            var message = string.Join("; ", result.Errors.Select(e => e.ErrorMessage));
            throw new DomainException(ErrorCodes.ValidationFailed, message);
        }
    }
}
