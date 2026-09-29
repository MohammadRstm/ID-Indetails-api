using FluentValidation;
using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Notifications;

public sealed class NotificationListQueryValidator : AbstractValidator<NotificationListQuery>
{
    public NotificationListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or more.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}
