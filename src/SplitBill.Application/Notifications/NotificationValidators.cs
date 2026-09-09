using FluentValidation;

namespace SplitBill.Application.Notifications;

public sealed class CreatePushSubscriptionRequestValidator : AbstractValidator<CreatePushSubscriptionRequest>
{
    public CreatePushSubscriptionRequestValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.P256dhKey).NotEmpty().MaximumLength(500);
        RuleFor(x => x.AuthKey).NotEmpty().MaximumLength(500);
    }
}
