using ID_Indetails.api.DTO.Response.Reports;

namespace ID_Indetails.api.Services.Reports.Abstraction;

/// <summary>
/// The Design employees who have logged no work this week (the dashboard's "not logging" item) and reminders for them
/// to log their hours. Needs report:view and Design, like the dashboard: anyone else gets not found. Each person is
/// reminded at most once a day, by anyone.
/// </summary>
public interface INotLoggingService
{
    /// <summary>The people, with their hours this week (only for callers with work_entry:view_all) and whether they were reminded today.</summary>
    Task<NotLoggingResponse> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reminds one person. Not found if they aren't a Design person; conflict (<c>logged_this_week</c>) if they are not on
    /// today's list, or (<c>already_reminded</c>) if someone reminded them today.
    /// </summary>
    Task<HoursRemindersResponse> RemindAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Reminds everyone on the list who wasn't reminded today. Nobody to remind is not an error.</summary>
    Task<HoursRemindersResponse> RemindAllAsync(CancellationToken cancellationToken = default);
}
