using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Requirements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Dashboard;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Dashboard.Abstraction;

namespace ID_Indetails.api.Services.Dashboard.Implementation;

/// <summary>A domain the caller can't see is not found.</summary>
public sealed class DashboardService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IMoneySummaryRepository summaries,
    IRequirementRepository requirements,
    IClock clock) : IDashboardService
{
    public const int LongestRunningCount = 3;
    public const int DeadlineDays = 14;
    public const int DeadlineItems = 8;

    public async Task<DashboardResponse> GetAsync(string domain, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var projects = await summaries.DomainProjectsAsync(domain, cancellationToken);
        var clients = await summaries.DomainClientsAsync(domain, cancellationToken);
        var today = clock.CompanyToday;

        return new DashboardResponse(
            domain,
            projects.Count(p => p.Status == ProjectStatus.Active),
            clients.Count(c => c.ArchivedAt is null),
            projects.GroupBy(p => p.Status).Select(g => new StatusTotalResponse(g.Key, g.Count())).OrderBy(s => s.Status).ToList(),
            projects
                .Where(p => p.Status is ProjectStatus.Active or ProjectStatus.Hold)
                .OrderBy(p => p.StartDate)
                .Take(LongestRunningCount)
                .Select(p => new RunningProjectResponse(p.Id, p.Title, p.StartDate, today.DayNumber - p.StartDate.DayNumber))
                .ToList());
    }

    public async Task<UpcomingDeadlinesResponse> DeadlinesAsync(string domain, CancellationToken cancellationToken = default)
    {
        await CheckDomainAsync(domain, cancellationToken);
        var today = clock.CompanyToday;
        var due = await requirements.OpenDueByAsync(domain, today.AddDays(DeadlineDays), cancellationToken);

        return new UpcomingDeadlinesResponse(
            due.Count,
            due.Count(r => RequirementRules.IsOverdue(r.Deadline, r.IsDone, today)),
            due.Take(DeadlineItems)
                .Select(r => new DeadlineResponse(
                    r.Id,
                    r.Text,
                    r.Deadline!.Value,
                    RequirementRules.IsOverdue(r.Deadline, r.IsDone, today),
                    r.Deadline!.Value.DayNumber - today.DayNumber,
                    new DeadlineProjectResponse(r.Project.Id, r.Project.Title, r.Project.Icon, r.Project.Tint),
                    r.Assignees
                        .Select(a => new PersonRefResponse(a.UserId, PersonName.Of(a.User.FirstName, a.User.LastName, a.User.Email)))
                        .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .ToList());
    }

    /// <summary>A domain the caller can't see is not found.</summary>
    private async Task CheckDomainAsync(string domain, CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!DomainCodes.All.Contains(domain) || !actor.CanSee(domain))
        {
            throw new NotFoundException("Domain not found.");
        }
    }
}
