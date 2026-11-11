using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Boqs.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;

namespace ID_Indetails.api.Services.Boqs.Implementation;

/// <summary>
/// Who may do what:
/// <list type="bullet">
/// <item>A site the caller can't see reads as 404; a Design project is 422 <c>not_for_this_domain</c>, an internal
/// Execution project 422 <c>boq_execution_only</c>.</item>
/// <item>Recording: <c>project:pricing_manage</c> on any open site, or <c>site_log:create</c> on a site the caller is on
/// the staff of (403 <c>not_on_site_team</c> otherwise), as for daily logs.</item>
/// <item>Reading: <c>financial:view</c> or <c>project:pricing_manage</c> (who see its BOQs), <c>site_log:view_all</c> (the
/// Execution admins who follow every site's daily logs), or the site's own staff. Prices and values only with
/// <c>financial:view</c>.</item>
/// </list>
/// </summary>
public sealed class BoqProgressService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    ISiteTeamRepository team,
    IBoqProgressRepository progress,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IBoqProgressService
{
    public async Task<BoqProgressResponse> GetAsync(long projectId, BoqProgressQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var site = Site(actor, await projects.GetAsync(projectId, cancellationToken));
        var seesMoney = actor.Has(PermissionCodes.FinancialView);
        if (!seesMoney && !actor.Has(PermissionCodes.ProjectPricingManage) && !actor.Has(PermissionCodes.SiteLogViewAll))
        {
            await RequireOnTeamAsync(actor, site, cancellationToken);
        }

        return await DescribeAsync(site, query.AsOf ?? clock.CompanyToday, seesMoney, cancellationToken);
    }

    public async Task<BoqProgressResponse> RecordAsync(long projectId, RecordBoqProgressRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        // the site's row is held until the records are saved, so two saves of the same day can't both add a line's record
        var site = Site(actor, await projects.GetForUpdateAsync(projectId, cancellationToken));
        SiteRules.CheckOpen(site.DomainCode, site.Status, site.RevenueLockedAt is not null).ThrowIfDenied();
        if (!actor.Has(PermissionCodes.ProjectPricingManage))
        {
            await RequireOnTeamAsync(actor, site, cancellationToken);
        }

        var today = clock.CompanyToday;
        var day = request.RecordedOn ?? today;
        BoqProgressRules.CheckDay(day, today).ThrowIfDenied();
        var lines = (await progress.AgreedLinesAsync(site.Id, cancellationToken)).ToDictionary(r => r.Line.Id, r => r.Line);
        var entries = request.Lines.Select(l => new BoqProgressRules.Entry(l.BoqLineId, l.ExecutedQuantity)).ToList();
        BoqProgressRules.CheckEntries(entries, lines).ThrowIfDenied();

        var existing = (await progress.OnDayForUpdateAsync(entries.Select(e => e.LineId).ToList(), day, cancellationToken))
            .ToDictionary(r => r.BoqLineId);
        var now = clock.UtcNow;
        var changed = 0;
        foreach (var line in request.Lines)
        {
            var note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim();
            if (existing.TryGetValue(line.BoqLineId, out var record))
            {
                // the same quantity and note again changes nothing
                if (record.ExecutedQuantity == line.ExecutedQuantity && record.Note == note)
                {
                    continue;
                }

                record.ExecutedQuantity = line.ExecutedQuantity;
                record.Note = note;
                record.RecordedById = actor.UserId;
                record.RecordedAt = now;
            }
            else
            {
                progress.Add(new BoqLineProgress
                {
                    BoqLineId = line.BoqLineId,
                    ProjectId = site.Id,
                    RecordedOn = day,
                    ExecutedQuantity = line.ExecutedQuantity,
                    Note = note,
                    RecordedById = actor.UserId,
                    RecordedAt = now,
                });
            }

            changed++;
        }

        if (changed > 0)
        {
            // quantities, not money: no amount, so the feed shows it to whoever sees the site
            activity.Add(new ActivityEvent
            {
                DomainCode = site.DomainCode,
                Kind = ActivityEventKinds.BoqProgressRecorded,
                OccurredAt = now,
                ActorId = actor.UserId,
                ProjectId = site.Id,
                ClientId = site.ClientId,
                SubjectType = "project",
                SubjectId = site.Id,
                Payload = JsonSerializer.Serialize(new { recordedOn = day, lines = changed }),
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(site, today, actor.Has(PermissionCodes.FinancialView), cancellationToken);
    }

    /// <summary>A site the caller sees, of Execution, with BOQs.</summary>
    private static Project Site(UserAccess actor, Project? project)
    {
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        if (project.DomainCode != DomainCodes.Execution)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution sites have progress by BOQ.");
        }

        BoqRules.CheckSite(project.DomainCode, project.BillingType).ThrowIfDenied();
        return project;
    }

    private async Task RequireOnTeamAsync(UserAccess actor, Project site, CancellationToken cancellationToken)
        => SiteTeamRules.CheckOnTeam(
                actor.Has(PermissionCodes.SiteLogCreate) && await team.IsOnTeamAsync(site.Id, actor.UserId, cancellationToken))
            .ThrowIfDenied();

    private async Task<BoqProgressResponse> DescribeAsync(Project site, DateOnly asOf, bool seesMoney, CancellationToken cancellationToken)
    {
        var rows = await progress.AgreedLinesAsync(site.Id, cancellationToken);
        var latest = await progress.LatestAsync(site.Id, asOf, cancellationToken);
        var sheet = BoqProgressRules.Of(rows.Select(r => r.Line), latest.ToDictionary(p => p.Key, p => p.Value.ExecutedQuantity));
        var byId = rows.ToDictionary(r => r.Line.Id);
        var counted = sheet.Lines.Select(f => f.Line.Id).ToList();
        var history = await progress.HistoryAsync(counted, asOf, BoqProgressRules.HistoryLimit, cancellationToken);

        BoqProgressLineResponse LineOf(BoqProgressRules.LineFigures f)
        {
            var row = byId[f.Line.Id];
            DateOnly? executedOn = f.Executed is not null && latest.TryGetValue(f.Line.Id, out var record) ? record.RecordedOn : null;
            return new BoqProgressLineResponse(
                f.Line.Id,
                f.Line.BoqId,
                f.Line.Kind,
                row.BoqNumber,
                f.Line.BoqTitle,
                row.Position,
                row.Code,
                row.Description,
                row.Unit,
                f.Quantity,
                f.TakesProgress,
                f.Executed,
                executedOn,
                f.Progress,
                seesMoney ? f.UnitPrice : null,
                seesMoney ? f.AgreedValue : null,
                seesMoney ? f.ValueExecuted : null);
        }

        return new BoqProgressResponse(
            site.Id,
            asOf,
            sheet.Lines.Select(LineOf).ToList(),
            seesMoney ? sheet.AgreedValue : null,
            seesMoney ? sheet.ValueExecuted : null,
            sheet.Recorded ? sheet.Progress : null,
            history.Select(h => new BoqProgressRecordResponse(
                h.Record.Id, h.Record.BoqLineId, h.Record.RecordedOn, h.Record.ExecutedQuantity, h.Record.Note,
                new PersonRefResponse(h.Record.RecordedById, h.RecordedByName), h.Record.RecordedAt)).ToList());
    }
}
