using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Proton;

public sealed class AskProtonRequestValidator : AbstractValidator<AskProtonRequest>
{
    public const int MaxTextLength = 2_000;

    /// <summary>The areas a page context can name: the two businesses and the company area.</summary>
    public static readonly IReadOnlyList<string> Areas = [DomainCodes.Design, DomainCodes.Execution, "company"];

    public AskProtonRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().When(x => Buttons(x) == 0).WithMessage("Ask a question.")
            .MaximumLength(MaxTextLength).WithMessage($"A question can be at most {MaxTextLength} characters.");
        RuleFor(x => x).Must(x => Buttons(x) <= 1).WithMessage("Pick one option at a time.");
        RuleFor(x => x.Page!).SetValidator(new ProtonPageContextValidator()).When(x => x.Page is not null);
        When(x => x.Choice is not null, () =>
        {
            RuleFor(x => x.Choice!.Type).IsInEnum().WithMessage("Pick one of the options.");
            RuleFor(x => x.Choice!.Id).GreaterThan(0).WithMessage("Pick one of the options.");
        });
        RuleFor(x => x.Reply).IsInEnum().WithMessage("Pick one of the options.");
        RuleFor(x => x.Suggestion).InclusiveBetween(0, ProtonUiRules.MaxSuggestions - 1).WithMessage("Pick one of the suggestions.");
    }

    /// <summary>How many of the last reply's buttons the request answers with.</summary>
    private static int Buttons(AskProtonRequest request)
        => (request.Choice is null ? 0 : 1) + (request.Reply is null ? 0 : 1) + (request.Suggestion is null ? 0 : 1);
}

/// <summary>A page is described by known values only: an area, a page, and a record only together with its kind.</summary>
public sealed class ProtonPageContextValidator : AbstractValidator<ProtonPageContext>
{
    public ProtonPageContextValidator()
    {
        RuleFor(x => x.Area).Must(a => AskProtonRequestValidator.Areas.Contains(a)).WithMessage("Unknown area.");
        RuleFor(x => x.Page).IsInEnum();
        RuleFor(x => x.Section).IsInEnum();
        RuleFor(x => x.Surface).IsInEnum();
        RuleFor(x => x.EntityType).IsInEnum().NotNull().When(x => x.EntityId is not null).WithMessage("A record needs its kind.");
        RuleFor(x => x.EntityId).NotNull().GreaterThan(0).When(x => x.EntityType is not null).WithMessage("A record needs its id.");
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
