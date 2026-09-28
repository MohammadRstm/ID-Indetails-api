using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Employees.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Execution.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;

namespace ID_Indetails.api.Services.Execution.Implementation;

public sealed class SiteRecordsService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IEmployeeRepository employees,
    ISiteRecordsRepository records,
    IProjectFinanceService finance,
    IClock clock) : ISiteRecordsService
{
    private const int RecentShown = 20;

    public async Task<IReadOnlyList<SiteSummaryResponse>> SummariesAsync(SiteSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var sites = await records.SitesAsync(query.Ids.Distinct().ToList(), actor.VisibleDomains, cancellationToken);
        if (sites.Count == 0)
        {
            return [];
        }

        var money = await finance.GetAsync(sites, cancellationToken);
        var activity = (await records.ActivityAsync(sites.Select(s => s.Id).ToList(), actor.UserId, cancellationToken))
            .ToDictionary(a => a.ProjectId);

        return query.Ids.Distinct()
            .Where(money.ContainsKey)
            .Select(id => new SiteSummaryResponse(
                id,
                activity[id].MaterialCount,
                money[id].Costs.Materials,
                activity[id].ReportedWeeks,
                money[id].Costs.GroundLabour,
                activity[id].LastRecordedAt))
            .ToList();
    }

    public async Task<SiteRecordsResponse> MineAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await DescribeAsync(actor, actor.UserId, cancellationToken);
    }

    public async Task<SiteRecordsResponse> OfAsync(long userId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await DescribeAsync(actor, userId, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<SiteRecordsResponse> DescribeAsync(UserAccess actor, long userId, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(userId, cancellationToken);
        if (person is null || (userId != actor.UserId && !EmployeeRules.CanSee(actor, person.Domain)))
        {
            throw new NotFoundException("Employee not found.");
        }

        var materials = await records.MaterialsByAsync(userId, actor.VisibleDomains, cancellationToken);
        var weeks = await records.LabourWeeksByAsync(userId, actor.VisibleDomains, cancellationToken);
        var all = materials
            .Select(m => new SiteRecords.Record(SiteRecords.Kind.Material, m.Id, m.ProjectId, clock.ToCompanyDate(m.AddedAt), m.AddedAt, m.Name, m.Cost))
            .Concat(weeks.Select(w => new SiteRecords.Record(
                SiteRecords.Kind.LabourWeek, w.Id, w.ProjectId, clock.ToCompanyDate(w.SubmittedAt), w.SubmittedAt, w.WeekStart.ToString("yyyy-MM-dd"), w.Total)))
            .ToList();

        var summary = SiteRecords.Summarise(all, clock.CompanyToday);
        var sites = (await records.SitesAsync(summary.Projects.Select(p => p.ProjectId).ToList(), actor.VisibleDomains, cancellationToken))
            .ToDictionary(p => p.Id);

        return new SiteRecordsResponse
        {
            Person = new PersonRefResponse(person.Id, $"{person.FirstName} {person.LastName}".Trim() is { Length: > 0 } name ? name : person.Email),
            MaterialCount = summary.MaterialCount,
            Materials = summary.Materials,
            LabourWeeks = summary.LabourWeeks,
            Labour = summary.Labour,
            Total = summary.Total,
            ThisQuarter = summary.ThisQuarter,
            ActiveDays = summary.ActiveDays,
            Weeks = summary.Weeks.Select(w => new SiteRecordsWeekResponse(w.WeekStart, w.Materials, w.Labour, w.Value)).ToList(),
            Projects = summary.Projects
                .Select(p => new SiteRecordsProjectResponse(
                    p.ProjectId, sites[p.ProjectId].Title, sites[p.ProjectId].Status, p.MaterialCount, p.Materials, p.LabourWeeks, p.Labour, p.LastRecordedAt))
                .ToList(),
            Recent = all
                .OrderByDescending(r => r.At)
                .Take(RecentShown)
                .Select(r => new SiteRecordResponse(r.Kind, r.Id, r.ProjectId, sites[r.ProjectId].Title, r.At, r.Label, r.Amount))
                .ToList(),
        };
    }
}
