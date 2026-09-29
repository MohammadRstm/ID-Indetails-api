using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Dashboard;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Dashboard.Abstraction;

namespace ID_Indetails.api.Services.Dashboard.Implementation;

/// <summary>A domain the caller can't see is not found.</summary>
public sealed class DashboardService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IMoneySummaryRepository summaries,
    IClock clock) : IDashboardService
{
    public const int LongestRunningCount = 3;

    public async Task<DashboardResponse> GetAsync(string domain, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!DomainCodes.All.Contains(domain) || !actor.CanSee(domain))
        {
            throw new NotFoundException("Domain not found.");
        }

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
}
