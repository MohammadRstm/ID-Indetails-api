using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

internal static class WorkEntryMapper
{
    /// <summary>When the current company month began, in UTC: work that starts earlier is in a closed month.</summary>
    public static DateTime MonthStartUtc(IClock clock)
        => clock.CompanyDayStartUtc(new DateOnly(clock.CompanyToday.Year, clock.CompanyToday.Month, 1));

    public static WorkEntryResponse ToResponse(WorkEntryView e, long viewerId, DomainSettings settings, IClock clock) => new(
        e.Id,
        new WorkEntryPersonResponse(e.UserId, e.UserName),
        new WorkEntryProjectResponse(e.ProjectId, e.ProjectTitle),
        e.Description,
        e.StartedAt,
        e.EndedAt,
        e.Minutes,
        e.CreatedAt,
        e.UpdatedAt,
        e.UserId == viewerId
            && WorkEntryRules.IsWithinEditWindow(e.CreatedAt, clock.UtcNow, settings.EditWindow)
            && WorkEntryRules.AcceptsWork(e.ProjectStatus, settings.HoldLogging)
            && WorkEntryRules.IsInOpenMonth(e.StartedAt, MonthStartUtc(clock)),
        e.CommentCount);
}
