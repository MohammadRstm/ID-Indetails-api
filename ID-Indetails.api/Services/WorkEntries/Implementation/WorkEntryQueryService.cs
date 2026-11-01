using System.Globalization;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
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
    ICompanyRepository company,
    IWorkEntryCommentRepository comments,
    IDomainSettingsService domainSettings,
    IProjectFinanceService projectFinance,
    IOvertimeService overtime,
    IClock clock) : IWorkEntryQueryService
{
    public async Task<PagedResponse<WorkEntryResponse>> ListAsync(WorkEntryListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.WorkEntryViewAll);
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
        var overtimeById = await overtime.OvertimeOfAsync([view, .. sameDay], cancellationToken);

        return new WorkEntryDetailResponse(
            WorkEntryMapper.ToResponse(view, actor.UserId, settings, clock, overtimeById),
            (await comments.ListAsync(id, cancellationToken))
                .Select(c => new WorkEntryCommentResponse(
                    c.Id, new WorkEntryCommentAuthorResponse(c.AuthorId, c.AuthorName, c.AuthorRole), c.Body, c.SentToGroup, c.CreatedAt))
                .ToList(),
            sameDay.Where(e => e.Id != id).Select(e => WorkEntryMapper.ToResponse(e, actor.UserId, settings, clock, overtimeById)).ToList());
    }

    public async Task<WorkEntryExport> ExportAsync(WorkEntryListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.WorkEntryViewAll);
        var rows = actor.CanSee(DomainCodes.Design)
            ? await entries.ListAsync(
                new WorkEntrySearch(query.UserId, query.ProjectId, query.ClientId, DayStart(query.From), DayEnd(query.To), query.Search),
                cancellationToken)
            : [];

        var userIds = rows.Select(e => e.UserId).Distinct().ToList();
        var people = (await entries.PeopleAsync(userIds, cancellationToken)).ToDictionary(p => p.Id);
        var calendar = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        var overtimeById = await overtime.OvertimeOfAsync(rows, cancellationToken);
        var withMoney = actor.Has(PermissionCodes.FinancialView);
        var payRates = withMoney ? await projectFinance.PayRatesAsync(userIds, cancellationToken) : [];
        var rates = payRates
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProjectFinance.PayRate>)g.OrderBy(r => r.EffectiveFrom).ToList());
        var paidOvertime = withMoney
            ? await projectFinance.PaidOvertimeAsync(
                rows.Select(e => new WorkEntryRow(e.Id, e.ProjectId, e.UserId, e.StartedAt, e.Minutes)).ToList(), payRates, cancellationToken)
            : new Dictionary<long, PaidOvertime>();

        var header = new List<string> { "Date", "Weekday", "Start", "End", "Hours", "Overtime hours", "Employee", "Email", "Team", "Project", "Client", "Task", "Outside scope" };
        if (withMoney)
        {
            header.AddRange(["Hourly rate", "Overtime paid hours", "Overtime rate", "Currency", "Labour cost"]);
        }

        header.Add("Entry id");
        var timeFormat = calendar.Use24HourTime ? "HH:mm" : "h:mm tt";
        var lines = rows.Select(e =>
        {
            var start = clock.ToCompanyTime(e.StartedAt);
            var person = people.GetValueOrDefault(e.UserId);
            var line = new List<string>
            {
                start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                start.ToString("ddd", CultureInfo.InvariantCulture),
                start.ToString(timeFormat, CultureInfo.InvariantCulture),
                clock.ToCompanyTime(e.EndedAt).ToString(timeFormat, CultureInfo.InvariantCulture),
                (e.Minutes / 60m).ToString("0.00", CultureInfo.InvariantCulture),
                (overtimeById.GetValueOrDefault(e.Id) / 60m).ToString("0.00", CultureInfo.InvariantCulture),
                e.UserName,
                person?.Email ?? string.Empty,
                person?.TeamName ?? string.Empty,
                e.ProjectTitle,
                e.ClientName ?? "Internal",
                e.Description,
                e.OutsideScope ? "Yes" : "No",
            };

            if (withMoney)
            {
                var day = DateOnly.FromDateTime(start);
                var rate = rates.TryGetValue(e.UserId, out var history) ? ProjectFinance.RateOn(history, day) : null;
                var worked = PaidOvertime.Of(e.UserId, day, e.Minutes, paidOvertime.GetValueOrDefault(e.Id));
                line.AddRange(rate is null
                    ? ["", "", "", "", ""]
                    : [
                        rate.HourlyCost.ToString("0.00", CultureInfo.InvariantCulture),
                        (worked.OvertimeMinutes / 60m).ToString("0.00", CultureInfo.InvariantCulture),
                        (rate.OvertimeRate * worked.OvertimeMultiplier).ToString("0.00", CultureInfo.InvariantCulture),
                        rate.Currency,
                        Math.Round(ProjectFinance.PriceLabour([worked], [rate]).Items.Single().Amount, 2, MidpointRounding.AwayFromZero)
                            .ToString("0.00", CultureInfo.InvariantCulture),
                    ]);
            }

            line.Add(e.Id.ToString(CultureInfo.InvariantCulture));
            return (IReadOnlyList<string>)line;
        });

        var from = query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "start";
        var to = (query.To ?? clock.CompanyToday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new WorkEntryExport($"work-entries-{from}-to-{to}.csv", Csv.Write(header, lines));
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
        var overtimeById = await overtime.OvertimeOfAsync(items, cancellationToken);
        return new PagedResponse<WorkEntryResponse>(
            items.Select(e => WorkEntryMapper.ToResponse(e, actor.UserId, settings, clock, overtimeById)).ToList(),
            search.Page,
            search.PageSize,
            total);
    }

    private DateTime? DayStart(DateOnly? day) => day is { } d ? clock.CompanyDayStartUtc(d) : null;

    private DateTime? DayEnd(DateOnly? day) => day is { } d ? clock.CompanyDayStartUtc(d.AddDays(1)) : null;
}
