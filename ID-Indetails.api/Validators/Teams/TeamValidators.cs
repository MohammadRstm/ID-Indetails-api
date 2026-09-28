using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Teams;

namespace ID_Indetails.api.Validators.Teams;

public sealed class CreateTeamRequestValidator : AbstractValidator<CreateTeamRequest>
{
    public CreateTeamRequestValidator()
    {
        RuleFor(x => x.Name).TeamName();
        RuleFor(x => x.DomainCode)
            .Must(d => d is null || DomainCodes.All.Contains(d))
            .WithMessage($"Domain must be one of: {string.Join(", ", DomainCodes.All)}.");
    }
}

public sealed class RenameTeamRequestValidator : AbstractValidator<RenameTeamRequest>
{
    public RenameTeamRequestValidator()
    {
        RuleFor(x => x.Name).TeamName();
    }
}

internal static class TeamRuleExtensions
{
    public static IRuleBuilderOptions<T, string> TeamName<T>(this IRuleBuilder<T, string> rule)
        => rule
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Team name is required.")
            .Must(n => n is null || n.Trim().Length <= 100).WithMessage("Team name must be at most 100 characters.");
}
