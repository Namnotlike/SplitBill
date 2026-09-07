using FluentValidation;
using SplitBill.Application.Common;

namespace SplitBill.Application.Groups;

public sealed class CreateGroupRequestValidator : AbstractValidator<CreateGroupRequest>
{
    public CreateGroupRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).NotEmpty();
        // Rỗng thì GroupService tự mặc định "VND" — chỉ validate khi người dùng có truyền giá trị
        // (CLAUDE.md mục 14: mỗi nhóm dùng cố định 1 trong danh sách tiền tệ được hỗ trợ).
        RuleFor(x => x.Currency)
            .Must(c => string.IsNullOrWhiteSpace(c) || SupportedCurrencies.IsSupported(c))
            .WithMessage($"Currency phải là một trong: {string.Join(", ", SupportedCurrencies.All.Keys)}.");
    }
}

public sealed class DuplicateGroupRequestValidator : AbstractValidator<DuplicateGroupRequest>
{
    public DuplicateGroupRequestValidator()
    {
        // Name rỗng/null hợp lệ (GroupService tự đặt "{Tên gốc} (bản sao)") — chỉ giới hạn độ dài khi
        // người dùng có truyền, khớp CreateGroupRequestValidator.
        RuleFor(x => x.Name).MaximumLength(200);
    }
}

public sealed class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.UserId is not null || !string.IsNullOrWhiteSpace(x.DisplayName))
            .WithMessage("Cần UserId hoặc DisplayName.");
    }
}

public sealed class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateMemberRoleRequestValidator : AbstractValidator<UpdateMemberRoleRequest>
{
    public UpdateMemberRoleRequestValidator()
    {
        RuleFor(x => x.Role).NotEmpty().Must(r => r is "Owner" or "Member").WithMessage("Role phải là 'Owner' hoặc 'Member'.");
    }
}
