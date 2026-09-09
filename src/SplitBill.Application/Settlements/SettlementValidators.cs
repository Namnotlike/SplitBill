using FluentValidation;

namespace SplitBill.Application.Settlements;

public sealed class CreateSettlementRequestValidator : AbstractValidator<CreateSettlementRequest>
{
    public CreateSettlementRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount phải > 0.");
        RuleFor(x => x)
            .Must(x => x.FromMemberId != x.ToMemberId)
            .WithMessage("FromMemberId và ToMemberId không được trùng nhau.");
    }
}

// Miễn nợ (CLAUDE.md mục 25.2) — cùng luật validate cơ bản với CreateSettlementRequest.
public sealed class WaiveSettlementRequestValidator : AbstractValidator<WaiveSettlementRequest>
{
    public WaiveSettlementRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount phải > 0.");
        RuleFor(x => x)
            .Must(x => x.FromMemberId != x.ToMemberId)
            .WithMessage("FromMemberId và ToMemberId không được trùng nhau.");
    }
}
