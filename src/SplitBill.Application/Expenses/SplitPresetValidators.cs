using FluentValidation;

namespace SplitBill.Application.Expenses;

public sealed class CreateSplitPresetRequestValidator : AbstractValidator<CreateSplitPresetRequest>
{
    public CreateSplitPresetRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SplitMode).NotEmpty();
    }
}
