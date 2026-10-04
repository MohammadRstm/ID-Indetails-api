using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Activity;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Activity;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Activity.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Activity.Implementation;

/// <summary>
/// The activity feed of the domains the caller can see. Work events need <c>work_entry:view_all</c>, amounts (and the
/// markup in <c>material_markup_set</c>'s payload) need <c>financial:view</c>, and company-level events (no domain:
/// overhead, working time) need <c>company:view</c>.
/// A <c>domain</c> filter keeps that domain's events only, so someone who sees both domains can look at one.
/// </summary>
public sealed class ActivityFeedService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IActivityFeedRepository feed,
    IWorkEntryRepository entries,
    IDomainSettingsService domainSettings,
    IOvertimeService overtime,
    IClock clock) : IActivityFeedService
{
    public async Task<PagedResponse<ActivityItemResponse>> ListAsync(ActivityListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var withCompany = actor.Has(PermissionCodes.CompanyView) && query.Domain is null;
        var domains = query.Domain is { } domain ? actor.VisibleDomains.Where(d => d == domain).ToList() : actor.VisibleDomains;
        var seesWork = actor.Has(PermissionCodes.WorkEntryViewAll);
        var seesMoney = actor.Has(PermissionCodes.FinancialView);
        if (query.Group == ActivityGroup.Company && !withCompany)
        {
            return new PagedResponse<ActivityItemResponse>([], query.Page, query.PageSize, 0);
        }

        var hidden = new List<string>(seesWork ? [] : ActivityGroups.KindsOf(ActivityGroup.Work));
        if (!actor.Has(PermissionCodes.SalaryView))
        {
            hidden.AddRange([ActivityEventKinds.HoursExcused, ActivityEventKinds.HoursExcuseRemoved]);
        }

        if (!actor.Has(PermissionCodes.CompanyView))
        {
            hidden.Add(ActivityEventKinds.ScheduleChanged);
        }

        var (items, total) = await feed.SearchAsync(
            new ActivityFeedSearch(
                domains,
                withCompany,
                query.Group is { } group and not ActivityGroup.Company ? ActivityGroups.KindsOf(group) : null,
                hidden,
                query.Group == ActivityGroup.Company,
                query.ProjectId,
                query.ClientId,
                query.UserId,
                query.Page,
                query.PageSize,
                query.Internal),
            cancellationToken);

        var workIds = items.Where(e => e.SubjectType == "work_entry" && e.SubjectId is not null).Select(e => e.SubjectId!.Value).Distinct().ToList();
        var work = workIds.Count > 0 ? (await entries.ViewsByIdAsync(workIds, cancellationToken)).ToDictionary(e => e.Id) : [];
        var overtimeById = await overtime.OvertimeOfAsync(work.Values, cancellationToken);
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);

        return new PagedResponse<ActivityItemResponse>(
            items.Select(e => new ActivityItemResponse(
                e.Id,
                e.Kind,
                ActivityGroups.GroupOf(e.Kind, e.DomainCode),
                e.DomainCode,
                e.OccurredAt,
                e.ActorId is { } a ? new ActivityRefResponse(a, e.ActorName ?? string.Empty) : null,
                e.ProjectId is { } p ? new ActivityRefResponse(p, e.ProjectTitle ?? string.Empty) : null,
                e.ClientId is { } c ? new ActivityRefResponse(c, e.ClientName ?? string.Empty) : null,
                e.SubjectType,
                e.SubjectId,
                seesMoney ? e.Amount : null,
                seesMoney ? e.PrevAmount : null,
                e.Payload is null || (!seesMoney && e.Kind == ActivityEventKinds.MaterialMarkupSet)
                    ? null
                    : JsonDocument.Parse(e.Payload).RootElement.Clone(),
                e.SubjectType == "work_entry" && e.SubjectId is { } id && work.TryGetValue(id, out var entry)
                    ? WorkEntryMapper.ToResponse(entry, actor.UserId, settings, clock, overtimeById)
                    : null,
                e.IsDemo)).ToList(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<ActivityTodayResponse> TodayAsync(CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!actor.CanSee(DomainCodes.Design))
        {
            throw new NotFoundException("Domain not found.");
        }

        var today = clock.CompanyToday;
        var rows = await entries.RowsAsync(
            new WorkEntrySearch(From: clock.CompanyDayStartUtc(today), To: clock.CompanyDayStartUtc(today.AddDays(1))),
            cancellationToken);

        return new ActivityTodayResponse(
            today,
            rows.Sum(r => r.Minutes),
            rows.Count,
            rows.Select(r => r.UserId).Distinct().Count(),
            rows.GroupBy(r => clock.ToCompanyTime(r.CreatedAt).Hour)
                .OrderBy(g => g.Key)
                .Select(g => new LoggedHourResponse(g.Key, g.Sum(r => r.Minutes)))
                .ToList());
    }
}
