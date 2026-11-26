using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.TimeOff.Abstraction;

/// <summary>
/// What time off means elsewhere: hours on its days wait for approval, no new meetings, the super admins' alert when
/// someone on it uses the app, and ending it when someone leaves Design's employees.
/// </summary>
public interface ITimeOffWatch
{
    /// <summary>The granted time off of these people that takes a day from <paramref name="from"/> to <paramref name="to"/>.</summary>
    Task<IReadOnlyList<(long UserId, TimeOffRules.Period Period)>> GrantedAsync(
        IReadOnlyCollection<long> userIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The person's granted time off that takes any of these days (none when there are no days).</summary>
    Task<IReadOnlyList<TimeOffRules.Period>> PeriodsOnAsync(long userId, IReadOnlyCollection<DateOnly> days, CancellationToken cancellationToken = default);

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
