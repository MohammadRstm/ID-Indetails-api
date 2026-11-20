using FluentValidation;
using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.Validators.Clients;

namespace ID_Indetails.api.Validators.Boqs;

/// <summary>
/// Shape of recorded progress. The column is (14,4); whether a line takes progress, the day and the most a line may hold
/// (twice its quantity) depend on the site and the company's day, so they are <see cref="BoqProgressRules"/>' to check.
/// </summary>
public sealed class RecordBoqProgressRequestValidator : AbstractValidator<RecordBoqProgressRequest>
{
    /// <summary>The largest quantity the column holds.</summary>
    public const decimal MaxQuantity = 9_999_999_999.9999m;

    public RecordBoqProgressRequestValidator()
    {
        RuleFor(x => x.Lines)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Record at least one line.")
            .Must(l => l.Count >= 1).WithMessage("Record at least one line.")
            .Must(l => l.Count <= BoqProgressRules.MaxLines).WithMessage($"Record at most {BoqProgressRules.MaxLines} lines at once.")
            .Must(l => l.Where(x => x is not null).GroupBy(x => x.BoqLineId).All(g => g.Count() == 1)).WithMessage("Record each line once.");
        RuleForEach(x => x.Lines)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A line is missing.")
            .ChildRules(line =>
            {
                line.RuleFor(l => l.BoqLineId).GreaterThan(0).WithMessage("Choose a BOQ line.");
                line.RuleFor(l => l.ExecutedQuantity)
                    .Cascade(CascadeMode.Stop)
                    .GreaterThanOrEqualTo(0).WithMessage("The executed quantity can't be negative.")
                    .LessThanOrEqualTo(MaxQuantity).WithMessage("The executed quantity is too large.")
                    .Must(q => q == Math.Round(q, 4)).WithMessage("The executed quantity can have at most four decimals.");
                line.RuleFor(l => l.Note).ClientText("Note", BoqProgressRules.MaxNoteLength);
            });
    }
}
