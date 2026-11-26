namespace ID_Indetails.api.DTO.Request.Projects;

/// <summary>Forcing a blocked change through. The body is optional for every kind but hours on a day of time off.</summary>
/// <param name="AsOvertime">
/// Hours on a day of time off (<c>work_entry_time_off</c>) only, and required there: true approves them as overtime,
/// false as normal hours. Ignored for every other kind.
/// </param>
public sealed record ForceBlockedChangeRequest(bool? AsOvertime);
