using FluentValidation;
using ID_Indetails.api.DTO.Request.BoardLayouts;

namespace ID_Indetails.api.Validators.BoardLayouts;

public sealed class SaveBoardLayoutRequestValidator : AbstractValidator<SaveBoardLayoutRequest>
{
    public const int MaxCards = 30;

    public SaveBoardLayoutRequestValidator()
    {
        RuleFor(x => x.Order)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Order is required.")
            .Must(o => o.Count is > 0 and <= MaxCards).WithMessage($"Order must list 1 to {MaxCards} cards.")
            .Must(o => o.Distinct().Count() == o.Count).WithMessage("Order can't list a card twice.");
        RuleForEach(x => x.Order)
            .NotEmpty().WithMessage("A card key is required.")
            .Matches("^[a-z0-9-]{1,40}$").WithMessage("Card keys are lowercase letters, digits and dashes, 40 characters at most.");
    }
}
