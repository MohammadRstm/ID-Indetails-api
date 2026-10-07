using FluentValidation;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Proton;

public sealed class AskProtonRequestValidator : AbstractValidator<AskProtonRequest>
{
    public const int MaxTextLength = 2_000;

    public AskProtonRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().WithMessage("Ask a question.")
            .MaximumLength(MaxTextLength).WithMessage($"A question can be at most {MaxTextLength} characters.");
    }
}

public sealed class TopUpProtonBudgetRequestValidator : AbstractValidator<TopUpProtonBudgetRequest>
{
    public const decimal MaxTopUpUsd = 10_000m;

    public TopUpProtonBudgetRequestValidator()
    {
        RuleFor(x => x.AmountUsd).Amount("Amount", allowZero: false)
            .LessThanOrEqualTo(MaxTopUpUsd).WithMessage($"A top-up can be at most {MaxTopUpUsd:N0} USD.");
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public sealed class DecideProtonActionRequestValidator : AbstractValidator<DecideProtonActionRequest>
{
    public DecideProtonActionRequestValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty().WithMessage("Which conversation is this from?");
    }
}
