using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// People holding <c>work_entry:view_all</c> read every Design entry; everyone else only their own.
/// An entry the caller may not read is not found.
/// </summary>
public sealed class WorkEntryQueryService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IWorkEntryRepository entries,
    IWorkEntryCommentRepository comments,
    IDomainSettingsService domainSettings,
    IClock clock) : IWorkEntryQueryService
{
    public async Task<PagedResponse<WorkEntryResponse>> ListAsync(WorkEntryListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        return await PageAsync(
            actor,
            new WorkEntrySearch(query.UserId, query.ProjectId, query.ClientId, DayStart(query.From), DayEnd(query.To), query.Search, query.Page, query.PageSize),
            cancellationToken);
    }

    public async Task<PagedResponse<WorkEntryResponse>> ListMineAsync(MyWorkEntryListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        return await PageAsync(
            actor,
            new WorkEntrySearch(actor.UserId, query.ProjectId, null, DayStart(query.From), DayEnd(query.To), query.Search, query.Page, query.PageSize),
            cancellationToken);
    }

    public async Task<WorkEntryDetailResponse> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var view = await entries.GetViewAsync(id, cancellationToken);
        if (view is null || !CanRead(actor, view))
        {
            throw new NotFoundException("Work entry not found.");
        }

        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        var day = clock.ToCompanyDate(view.StartedAt);
        var sameDay = await entries.ListAsync(
            new WorkEntrySearch(view.UserId, view.ProjectId, From: clock.CompanyDayStartUtc(day), To: clock.CompanyDayStartUtc(day.AddDays(1))),
            cancellationToken);

        return new WorkEntryDetailResponse(
            WorkEntryMapper.ToResponse(view, actor.UserId, settings, clock.UtcNow),
            (await comments.ListAsync(id, cancellationToken))
                .Select(c => new WorkEntryCommentResponse(
                    c.Id, new WorkEntryCommentAuthorResponse(c.AuthorId, c.AuthorName, c.AuthorRole), c.Body, c.SentToGroup, c.CreatedAt))
                .ToList(),
            sameDay.Where(e => e.Id != id).Select(e => WorkEntryMapper.ToResponse(e, actor.UserId, settings, clock.UtcNow)).ToList());
    }

    /// <summary>Whether the caller may read this entry: anyone's with view_all, their own with view_own.</summary>
    public static bool CanRead(UserAccess actor, WorkEntryView entry)
        => actor.CanSee(entry.ProjectDomain)
            && (actor.Has(PermissionCodes.WorkEntryViewAll)
                || (entry.UserId == actor.UserId && actor.Has(PermissionCodes.WorkEntryViewOwn)));

    private async Task<PagedResponse<WorkEntryResponse>> PageAsync(UserAccess actor, WorkEntrySearch search, CancellationToken cancellationToken)
    {
        if (!actor.CanSee(DomainCodes.Design))
        {
            return new PagedResponse<WorkEntryResponse>([], search.Page, search.PageSize, 0);
        }

        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        var (items, total) = await entries.SearchAsync(search, cancellationToken);
        return new PagedResponse<WorkEntryResponse>(
            items.Select(e => WorkEntryMapper.ToResponse(e, actor.UserId, settings, clock.UtcNow)).ToList(),
            search.Page,
            search.PageSize,
            total);
    }

    private DateTime? DayStart(DateOnly? day) => day is { } d ? clock.CompanyDayStartUtc(d) : null;

    private DateTime? DayEnd(DateOnly? day) => day is { } d ? clock.CompanyDayStartUtc(d.AddDays(1)) : null;
}
