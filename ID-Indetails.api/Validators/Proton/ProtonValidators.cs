using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Proton;

public sealed class AskProtonRequestValidator : AbstractValidator<AskProtonRequest>
{
    public const int MaxTextLength = 2_000;

    /// <summary>The areas a page context can name: the two businesses, the Website area and the company area.</summary>
    public static readonly IReadOnlyList<string> Areas = [DomainCodes.Design, DomainCodes.Execution, DomainCodes.Website, "company"];

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
        When(x => x.Voice is not null, () =>
        {
            RuleFor(x => x).Must(x => Buttons(x) == 0).WithMessage("A spoken question can't also pick an option.");
            RuleFor(x => x.ConversationId).NotEmpty().WithMessage("A spoken question belongs to its conversation.");
            RuleFor(x => x.Voice!.SessionId).Must(ProtonVoiceIds.IsValid).WithMessage("Unknown voice session.");
            RuleFor(x => x.Voice!.DelegationId).Must(ProtonVoiceIds.IsValid).WithMessage("Unknown spoken question.");
        });
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
        RuleFor(x => x.Part).IsInEnum();
        RuleFor(x => x.Surface).IsInEnum();
        RuleFor(x => x.EntityType).IsInEnum().NotNull().When(x => x.EntityId is not null).WithMessage("A record needs its kind.");
        RuleFor(x => x.EntityId).NotNull().GreaterThan(0).When(x => x.EntityType is not null).WithMessage("A record needs its id.");
    }
}

/// <summary>The provider's ids are opaque, but short and plain: letters, digits, "_" and "-".</summary>
public static class ProtonVoiceIds
{
    public const int MaxLength = 128;

    public static bool IsValid(string? id)
        => id is { Length: > 0 and <= MaxLength } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

/// <summary>A WebRTC offer: a session description, of a size a browser's audio offer has.</summary>
public sealed class StartProtonVoiceRequestValidator : AbstractValidator<StartProtonVoiceRequest>
{
    public const int MaxSdpLength = 20_000;

    public StartProtonVoiceRequestValidator()
    {
        RuleFor(x => x.Sdp).NotEmpty().WithMessage("The browser's connection offer is missing.")
            .MaximumLength(MaxSdpLength).WithMessage("The browser's connection offer is too long.")
            .Must(s => s.StartsWith("v=0", StringComparison.Ordinal)).WithMessage("That isn't a connection offer.");
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

public sealed class SaveProtonMemoryRequestValidator : AbstractValidator<SaveProtonMemoryRequest>
{
    public SaveProtonMemoryRequestValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty().WithMessage("Which conversation is this from?");
        RuleFor(x => x.Text).NotEmpty().WithMessage("What should I remember?")
            .MaximumLength(Business.Proton.ProtonMemoryRules.MaxLength)
            .WithMessage($"A memory is at most {Business.Proton.ProtonMemoryRules.MaxLength} characters.");
    }
}

public sealed class DecideProtonActionRequestValidator : AbstractValidator<DecideProtonActionRequest>
{
    public DecideProtonActionRequestValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty().WithMessage("Which conversation is this from?");
        RuleFor(x => x.Typed).MaximumLength(200);
    }
}

public sealed class ProtonConversationListQueryValidator : AbstractValidator<ProtonConversationListQuery>
{
    public const int MaxPageSize = 50;

    public ProtonConversationListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize).WithMessage($"Page size must be between 1 and {MaxPageSize}.");
    }
}

public sealed class RenameProtonConversationRequestValidator : AbstractValidator<RenameProtonConversationRequest>
{
    public RenameProtonConversationRequestValidator()
    {
        RuleFor(x => x.Title).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Give the chat a name.")
            .MaximumLength(Configuration.ModelConfig.ProtonStoredConversationConfiguration.TitleLength)
            .WithMessage($"A chat's name can be at most {Configuration.ModelConfig.ProtonStoredConversationConfiguration.TitleLength} characters.")
            .Must(t => t.All(c => !char.IsControl(c))).WithMessage("A chat's name is one line of text.");
    }
}
