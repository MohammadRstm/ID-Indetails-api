using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.TimeOff.Abstraction;

/// <summary>
/// What time off means elsewhere: no hours on its days or while it runs, no new meetings, the super admins' alerts when
/// someone on it is active, and ending it when someone leaves Design's employees.
/// </summary>
public interface ITimeOffWatch
{
    /// <summary>The granted time off of these people that takes a day from <paramref name="from"/> to <paramref name="to"/>.</summary>
    Task<IReadOnlyList<(long UserId, TimeOffRules.Period Period)>> GrantedAsync(
        IReadOnlyCollection<long> userIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refuses hours on a day off (<paramref name="entryDays"/>, the company days an entry touches), and any change to
    /// hours while the person is off, telling the super admins (at most once an hour) when they tried it themselves.
    /// Someone acting for the person (forced or released hours) is held only to the days.
    /// </summary>
    Task CheckHoursAsync(long userId, IReadOnlyCollection<DateOnly> entryDays, CancellationToken cancellationToken = default);

    /// <summary>The person was active: tells the super admins, once a day, when they are off today. Never throws.</summary>
    Task SeenAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Refuses putting people in a meeting on a day they are off.</summary>
    Task CheckMeetingAsync(IReadOnlyCollection<long> attendeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Someone stops being one of Design's employees today: their pending requests are cancelled and their approved time
    /// off ends, keeping the days already taken; the super admins are told. Staged for the caller to save.
    /// </summary>
    Task EndAsync(User person, long actorId, string why, CancellationToken cancellationToken = default);

    /// <summary>Who hears that someone is off or back: their department's active people and the super admins, never them.</summary>
    Task<IReadOnlyList<long>> HeadsUpRecipientsAsync(long userId, long? teamId, CancellationToken cancellationToken = default);
}
