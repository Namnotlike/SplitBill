using FluentValidation;

namespace SplitBill.Application.Groups;

public sealed class CreateGroupTemplateRequestValidator : AbstractValidator<CreateGroupTemplateRequest>
{
    public CreateGroupTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Type).NotEmpty();
        RuleFor(x => x.MemberNames).Must(names => names is null || names.Count <= 50)
            .WithMessage("Mẫu nhóm chỉ lưu tối đa 50 tên thành viên.");
        RuleForEach(x => x.MemberNames).NotEmpty().MaximumLength(100)
            .When(x => x.MemberNames is not null);
    }
}
