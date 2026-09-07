using FluentValidation;

namespace SplitBill.Application.Expenses;

public sealed class CreateExpenseCommentRequestValidator : AbstractValidator<CreateExpenseCommentRequest>
{
    public CreateExpenseCommentRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(2000);
    }
}
