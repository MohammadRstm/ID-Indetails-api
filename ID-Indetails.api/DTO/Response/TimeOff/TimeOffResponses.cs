using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.TimeOff;

public sealed record TimeOffPersonResponse(long Id, string Name, long? PhotoFileId, string? DepartmentName);

/// <summary>A meeting the person is already in during the time off: it stays.</summary>
public sealed record TimeOffMeetingResponse(long Id, string? Topic, DateTime StartsAt, DateTime EndsAt);

/// <param name="Person">Who asked; left out of an employee's own list.</param>
/// <param name="WorkingDays">Their working days from the first day to the last.</param>
/// <param name="DaysTaken">Working days actually taken off: all of them once approved, the ones before the rejection day once rejected.</param>
/// <param name="DecidedByName">Who approved or declined it; left out of an employee's own list.</param>
/// <param name="EndedByName">Who rejected it; left out of an employee's own list.</param>
/// <param name="TakenThisYear">Working days of time off they have taken this year, for the super admins.</param>
/// <param name="Meetings">Meetings they are already in during it, for the super admins deciding it.</param>
public sealed record TimeOffResponse(
    long Id,
    TimeOffPersonResponse? Person,
    DateOnly FirstDay,
    DateOnly LastDay,
    string Reason,
    TimeOffStatus Status,
    bool? IsPaid,
    int WorkingDays,
    int DaysTaken,
    DateTime RequestedAt,
    DateTime? DecidedAt,
    string? DecidedByName,
    string? DecisionNote,
    DateOnly? EndedOn,
    DateTime? EndedAt,
    string? EndedByName,
    string? EndNote,
    int? TakenThisYear = null,
    IReadOnlyList<TimeOffMeetingResponse>? Meetings = null);

/// <summary>Someone off from <paramref name="FirstDay"/> to <paramref name="LastDay"/>: never whether it is paid, nor why.</summary>
public sealed record AwayResponse(long UserId, DateOnly FirstDay, DateOnly LastDay);
