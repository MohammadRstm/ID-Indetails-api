using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Execution.Abstraction;

namespace ID_Indetails.api.Services.Execution.Implementation;

public sealed class SiteLogService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    ISiteTeamRepository team,
    IUnitOfWork unitOfWork,
    IClock clock) : ISiteLogService
{
    public async Task<SiteLogResponse> CreateAsync(long projectId, SaveSiteLogRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.SiteLogCreate);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
        Throw(SiteTeamRules.CheckOnTeam(await team.IsOnTeamAsync(projectId, actor.UserId, cancellationToken)));

        var log = new SiteLog { ProjectId = projectId, UserId = actor.UserId, Body = request.Body.Trim(), CreatedAt = clock.UtcNow };
        team.AddLog(log);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(actor, log.Id, cancellationToken);
    }

    public async Task<SiteLogResponse> UpdateAsync(long id, SaveSiteLogRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.SiteLogCreate);
        var log = await team.GetLogForUpdateAsync(id, cancellationToken);
        // someone else's log reads as not found, whatever the caller may read
        if (log is null || log.UserId != actor.UserId)
        {
            throw new NotFoundException("Log not found.");
        }

        Throw(SiteTeamRules.CheckEditable(clock.ToCompanyDate(log.CreatedAt), clock.CompanyToday));
        log.Body = request.Body.Trim();
        log.UpdatedAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(actor, log.Id, cancellationToken);
    }

    public async Task<PagedResponse<SiteLogResponse>> MineAsync(SiteLogListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.SiteLogCreate);
        return await PageAsync(actor, query with { UserId = actor.UserId }, cancellationToken);
    }

    public async Task<PagedResponse<SiteLogResponse>> ListAsync(SiteLogListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.SiteLogViewAll);
        return await PageAsync(actor, query, cancellationToken);
    }

    public async Task<IReadOnlyList<MySiteResponse>> MySitesAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return (await team.SitesOfAsync(actor.UserId, cancellationToken))
            .Select(s => new MySiteResponse(s.Id, s.Title, s.ClientName, s.Status, s.Location, s.AssignedAt))
            .ToList();
    }

    private async Task<PagedResponse<SiteLogResponse>> PageAsync(UserAccess actor, SiteLogListQuery query, CancellationToken cancellationToken)
    {
        var (items, total) = await team.SearchLogsAsync(new SiteLogSearch(
            actor.VisibleBusinesses,
            query.UserId,
            query.ProjectId,
            query.From is { } from ? clock.CompanyDayStartUtc(from) : null,
            query.To is { } to ? clock.CompanyDayStartUtc(to.AddDays(1)) : null,
            query.Search?.Trim(),
            query.Page,
            query.PageSize), cancellationToken);
        return new PagedResponse<SiteLogResponse>(items.Select(r => ToResponse(actor, r)).ToList(), query.Page, query.PageSize, total);
    }

    private async Task<SiteLogResponse> DescribeAsync(UserAccess actor, long id, CancellationToken cancellationToken)
        => ToResponse(actor, await team.GetLogAsync(id, cancellationToken) ?? throw new NotFoundException("Log not found."));

    private SiteLogResponse ToResponse(UserAccess actor, SiteLogRow r) => new(
        r.Log.Id,
        new SiteLogProjectResponse(r.Log.ProjectId, r.ProjectTitle),
        new PersonRefResponse(r.Log.UserId, r.UserName),
        r.Log.Body,
        r.Log.CreatedAt,
        r.Log.UpdatedAt,
        r.Log.UserId == actor.UserId && SiteTeamRules.IsEditable(clock.ToCompanyDate(r.Log.CreatedAt), clock.CompanyToday));

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
