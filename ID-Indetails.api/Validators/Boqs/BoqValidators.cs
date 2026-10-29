using FluentValidation;
using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Boqs;

/// <summary>Shape of a new BOQ. What depends on the project or on who asks is the service's.</summary>
public sealed class CreateBoqRequestValidator : AbstractValidator<CreateBoqRequest>
{
    public CreateBoqRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Kind must be internal, client or vo.");
        RuleFor(x => x.Title).ClientText("Title", BoqRules.MaxTitleLength);
        RuleFor(x => x.Reason).ClientText("Reason", BoqRules.MaxReasonLength, required: true).When(x => x.Kind == BoqKind.Vo);
        RuleFor(x => x.Reason).Must(string.IsNullOrWhiteSpace).WithMessage("Only a variation order has a reason.").When(x => x.Kind != BoqKind.Vo);
        RuleFor(x => x.FromBoqId).GreaterThan(0).When(x => x.FromBoqId is not null).WithMessage("Choose a BOQ to copy from.");
    }
}

/// <summary>
/// Shape of a saved BOQ. The columns are quantity and rate (14,4), waste (6,4) and the totals (14,2); the sums
/// are kept inside them by <see cref="BoqRules.CheckContent"/>.
/// </summary>
public sealed class SaveBoqRequestValidator : AbstractValidator<SaveBoqRequest>
{
    public SaveBoqRequestValidator()
    {
        RuleFor(x => x.Title).ClientText("Title", BoqRules.MaxTitleLength, required: true);
        RuleFor(x => x.Reason).ClientText("Reason", BoqRules.MaxReasonLength);
        RuleFor(x => x.AdjustmentType).IsInEnum().WithMessage("Adjustment type must be none, percent or total.");
        RuleFor(x => x.AdjustmentValue)
            .Must((x, value) => (x.AdjustmentType == BoqAdjustmentType.None) == (value is null))
            .WithMessage("Give the adjustment value with a percent or total adjustment, and only then.");
        RuleFor(x => x.AdjustmentValue)
            .Must(v => v is null || (v >= BoqRules.MinPercent && v <= BoqRules.MaxPercent && decimal.Round(v.Value, 4) == v))
            .When(x => x.AdjustmentType == BoqAdjustmentType.Percent)
            .WithMessage($"A percent adjustment is a fraction from {BoqRules.MinPercent} to {BoqRules.MaxPercent}, with at most 4 decimals.");
        RuleFor(x => x.AdjustmentValue)
            .Must(v => v is null || (Math.Abs(v.Value) <= BoqRules.MaxAmount && decimal.Round(v.Value, 2) == v))
            .When(x => x.AdjustmentType == BoqAdjustmentType.Total)
            .WithMessage("The new total is too large, or has more than 2 decimals.");
        RuleFor(x => x.Lines)
            .Must(lines => lines is { Count: <= BoqRules.MaxLines })
            .WithMessage($"A BOQ has at most {BoqRules.MaxLines} lines.");
        RuleFor(x => x.Lines)
            .Must(lines => lines is null || lines.Where(l => l.Id is not null).GroupBy(l => l.Id).All(g => g.Count() == 1))
            .WithMessage("A line can only appear once.");
        RuleForEach(x => x.Lines).SetValidator(new SaveBoqLineRequestValidator());
    }
}

public sealed class SaveBoqLineRequestValidator : AbstractValidator<SaveBoqLineRequest>
{
    private const decimal MaxFigure = 999_999_999m;

    public SaveBoqLineRequestValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).When(x => x.Id is not null).WithMessage("The line's id must be one of this BOQ's lines.");
        RuleFor(x => x.Code).ClientText("Code", BoqRules.MaxCodeLength);
        RuleFor(x => x.Description).ClientText("Description", BoqRules.MaxDescriptionLength, required: true);
        RuleFor(x => x.Unit).ClientText("Unit", BoqRules.MaxUnitLength);
        RuleFor(x => x.Quantity)
            .Must(v => v >= 0).WithMessage("Quantity can't be negative.")
            .Must(v => v <= MaxFigure).WithMessage("Quantity is too large.")
            .Must(v => decimal.Round(v, 4) == v).WithMessage("Quantity can have at most 4 decimals.");
        RuleFor(x => x.WasteRate)
            .Must(v => v is >= 0 and <= 1).WithMessage("Waste is a fraction from 0 to 1 (0.05 is 5%).")
            .Must(v => decimal.Round(v, 4) == v).WithMessage("Waste can have at most 4 decimals.");
        RuleFor(x => x.Rate)
            .Must(v => Math.Abs(v) <= MaxFigure).WithMessage("Rate is too large.")
            .Must(v => decimal.Round(v, 4) == v).WithMessage("Rate can have at most 4 decimals.");
    }
}

public sealed class CopyBoqLinesRequestValidator : AbstractValidator<CopyBoqLinesRequest>
{
    public CopyBoqLinesRequestValidator()
    {
        RuleFor(x => x.FromBoqId).GreaterThan(0).WithMessage("Choose a BOQ to copy from.");
        RuleFor(x => x.LineIds)
            .Must(ids => ids is null || (ids.Count <= BoqRules.MaxLines && ids.All(id => id > 0)))
            .WithMessage($"Choose up to {BoqRules.MaxLines} lines to copy.");
    }
}

public sealed class WithdrawBoqAgreementRequestValidator : AbstractValidator<WithdrawBoqAgreementRequest>
{
    public WithdrawBoqAgreementRequestValidator()
    {
        RuleFor(x => x.Reason).ClientText("Reason", BoqRules.MaxReasonLength, required: true);
    }
}

public sealed class EmailBoqRequestValidator : AbstractValidator<EmailBoqRequest>
{
    public EmailBoqRequestValidator()
    {
        RuleFor(x => x.To).ValidEmail();
        RuleFor(x => x.Note).ClientText("Note", 500);
    }
}

public sealed class RecordBoqDeliveryRequestValidator : AbstractValidator<RecordBoqDeliveryRequest>
{
    public RecordBoqDeliveryRequestValidator()
    {
        RuleFor(x => x.Channel).IsInEnum().WithMessage("Channel must be email, whatsapp or download.");
        RuleFor(x => x.Recipient).ClientText("Recipient", 255);
        RuleFor(x => x.Recipient!).ValidEmail().When(x => x.Channel == DeliveryChannel.Email && x.Recipient is not null);
        RuleFor(x => x.Note).ClientText("Note", 500);
    }
}
