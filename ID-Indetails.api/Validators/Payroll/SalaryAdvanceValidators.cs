using FluentValidation;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Payroll;

public sealed class AskSalaryAdvanceRequestValidator : AbstractValidator<AskSalaryAdvanceRequest>
{
    public AskSalaryAdvanceRequestValidator()
    {
        RuleFor(x => x.Amount).Amount("The amount", allowZero: false);
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Say why you need the advance.")
            .MaximumLength(AdvanceRules.MaxNoteLength).WithMessage($"The reason must be at most {AdvanceRules.MaxNoteLength} characters.");
    }
}

public sealed class PlannedMonthRequestValidator : AbstractValidator<PlannedMonthRequest>
{
    public PlannedMonthRequestValidator()
    {
        RuleFor(x => x.Month).Must(m => m.Day == 1).WithMessage("A month is given as its first day.");
        RuleFor(x => x.Amount).Amount("Each month's repayment", allowZero: false);
    }
}

public sealed class ApproveSalaryAdvanceRequestValidator : AbstractValidator<ApproveSalaryAdvanceRequest>
{
    public ApproveSalaryAdvanceRequestValidator()
    {
        RuleFor(x => x.Amount).Amount("The amount", allowZero: false);
        RuleFor(x => x.Months).NotEmpty().WithMessage("Choose the months it comes off.");
        RuleForEach(x => x.Months).SetValidator(new PlannedMonthRequestValidator());
        RuleFor(x => x.Note).MaximumLength(AdvanceRules.MaxNoteLength).WithMessage($"The note must be at most {AdvanceRules.MaxNoteLength} characters.");
    }
}

public sealed class RecordSalaryAdvanceRequestValidator : AbstractValidator<RecordSalaryAdvanceRequest>
{
    public RecordSalaryAdvanceRequestValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("Choose who it is for.");
        RuleFor(x => x.Amount).Amount("The amount", allowZero: false);
        RuleFor(x => x.Months).NotEmpty().WithMessage("Choose the months it comes off.");
        RuleForEach(x => x.Months).SetValidator(new PlannedMonthRequestValidator());
        RuleFor(x => x.Note).MaximumLength(AdvanceRules.MaxNoteLength).WithMessage($"The note must be at most {AdvanceRules.MaxNoteLength} characters.");
    }
}

public sealed class SalaryAdvanceMonthsRequestValidator : AbstractValidator<SalaryAdvanceMonthsRequest>
{
    public SalaryAdvanceMonthsRequestValidator()
    {
        RuleFor(x => x.Months).NotEmpty().WithMessage("Choose the months it comes off.");
        RuleForEach(x => x.Months).SetValidator(new PlannedMonthRequestValidator());
    }
}

public sealed class SalaryAdvanceNoteRequestValidator : AbstractValidator<SalaryAdvanceNoteRequest>
{
    public SalaryAdvanceNoteRequestValidator()
        => RuleFor(x => x.Note).MaximumLength(AdvanceRules.MaxNoteLength).WithMessage($"The note must be at most {AdvanceRules.MaxNoteLength} characters.");
}

public sealed class SettleSalaryAdvanceRequestValidator : AbstractValidator<SettleSalaryAdvanceRequest>
{
    public SettleSalaryAdvanceRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Choose cash or written off.");
        RuleFor(x => x.Note).MaximumLength(AdvanceRules.MaxNoteLength).WithMessage($"The note must be at most {AdvanceRules.MaxNoteLength} characters.");
    }
}

public sealed class SalaryAdvanceListQueryValidator : AbstractValidator<SalaryAdvanceListQuery>
{
    public SalaryAdvanceListQueryValidator()
    {
        RuleFor(x => x.View).IsInEnum().WithMessage("View must be pending, open or closed.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}
