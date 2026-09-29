using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

internal static class WorkEntryMapper
{
    /// <summary>When the current company month began, in UTC: work that starts earlier is in a closed month.</summary>
    public static DateTime MonthStartUtc(IClock clock)
        => clock.CompanyDayStartUtc(new DateOnly(clock.CompanyToday.Year, clock.CompanyToday.Month, 1));

    /// <summary>The overtime of these entries by id (see <see cref="IOvertimeService"/>).</summary>
    public static Task<IReadOnlyDictionary<long, int>> OvertimeOfAsync(
        this IOvertimeService overtime,
        IEnumerable<WorkEntryView> entries,
        CancellationToken cancellationToken)
        => overtime.ByEntryAsync(entries.Select(e => new LoggedWork(e.Id, e.UserId, e.StartedAt)).ToList(), cancellationToken);

    public static Task<IReadOnlyDictionary<long, int>> OvertimeOfAsync(
        this IOvertimeService overtime,
        IEnumerable<WorkRow> rows,
        CancellationToken cancellationToken)
        => overtime.ByEntryAsync(rows.Select(r => new LoggedWork(r.Id, r.UserId, r.StartedAt)).ToList(), cancellationToken);

    /// <param name="overtime">Overtime minutes by entry id, from <see cref="OvertimeOfAsync"/>.</param>
    public static WorkEntryResponse ToResponse(
        WorkEntryView e,
        long viewerId,
        DomainSettings settings,
        IClock clock,
        IReadOnlyDictionary<long, int> overtime) => new(
        e.Id,
        new WorkEntryPersonResponse(e.UserId, e.UserName),
        new WorkEntryProjectResponse(e.ProjectId, e.ProjectTitle),
        e.Description,
        e.StartedAt,
        e.EndedAt,
        e.Minutes,
        overtime.GetValueOrDefault(e.Id),
        e.CreatedAt,
        e.UpdatedAt,
        e.UserId == viewerId
            && WorkEntryRules.IsWithinEditWindow(e.CreatedAt, clock.UtcNow, settings.EditWindow)
            && WorkEntryRules.AcceptsWork(e.ProjectStatus, settings.HoldLogging)
            && WorkEntryRules.IsInOpenMonth(e.StartedAt, MonthStartUtc(clock)),
        e.CommentCount);
}
