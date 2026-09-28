using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Labour.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Execution.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;

namespace ID_Indetails.api.Services.Execution.Implementation;

/// <summary>Cash and cost come from the shared finance calculator; only the projection is worked out here.</summary>
public sealed class SiteMoneyService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    ILabourRepository labour,
    IProjectFinanceService finance,
    IClock clock) : ISiteMoneyService
{
    public async Task<SiteMoneyTimelineResponse> TimelineAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        if (project.DomainCode != DomainCodes.Execution)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution projects have a site money timeline.");
        }

        var today = clock.CompanyToday;
        var weeks = await finance.CashByWeekAsync(project, clock.ToCompanyDate(project.FinalizedAt ?? clock.UtcNow), cancellationToken);
        var money = await finance.GetAsync(project, cancellationToken);
        var defaults = LabourRules.DefaultsAt(
            (await labour.ListDefaultsAsync(project.Id, cancellationToken))
                .Select(r => new LabourRules.Defaults(r.Default.EffectiveFrom, r.Default.Workers, r.Default.DailyPay))
                .ToList(),
            today);

        var cashProfit = money.CashProfit ?? 0;
        var projection = LabourRules.ProjectZero(
            project.Status is ProjectStatus.Completed or ProjectStatus.Archived,
            cashProfit,
            money.Summary.Profit,
            defaults,
            today);

        return new SiteMoneyTimelineResponse(
            weeks.Select(w => new CashWeekResponse(w.WeekStart, w.Received, w.Spent, w.GroundLabour, w.Profit, w.ReceivedInWeek, w.GroundLabourInWeek)).ToList(),
            new SiteProjectionResponse(
                projection.State,
                defaults?.Workers ?? 0,
                defaults?.DailyPay ?? 0,
                projection.DailyCost,
                cashProfit,
                money.Summary.Profit,
                projection.ZeroOn,
                projection.WorkingDays,
                projection.ZeroOnOncePaid,
                projection.WorkingDaysOncePaid),
            PaymentRules.BaseCurrency);
    }
}
