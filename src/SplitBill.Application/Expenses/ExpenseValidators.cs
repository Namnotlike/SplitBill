using FluentValidation;

namespace SplitBill.Application.Expenses;

public sealed class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TotalAmount).GreaterThan(0).WithMessage("TotalAmount phải > 0.");
        RuleFor(x => x.ExtraFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Payers).NotEmpty().WithMessage("Payers không được rỗng.");
        RuleFor(x => x.SplitMode).NotEmpty();
    }
}

public sealed class UpdateExpenseRequestValidator : AbstractValidator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TotalAmount).GreaterThan(0).WithMessage("TotalAmount phải > 0.");
        RuleFor(x => x.ExtraFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Payers).NotEmpty().WithMessage("Payers không được rỗng.");
        RuleFor(x => x.SplitMode).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("Thiếu RowVersion để kiểm tra đồng thời.");
    }
}

public sealed class PreviewSplitRequestValidator : AbstractValidator<PreviewSplitRequest>
{
    public PreviewSplitRequestValidator()
    {
        RuleFor(x => x.TotalAmount).GreaterThan(0);
        RuleFor(x => x.ExtraFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SplitMode).NotEmpty();
    }
}
