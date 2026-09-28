using System.Text.Json;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Activity;

public sealed record ActivityRefResponse(long Id, string Name);

/// <param name="Amount">Money events only, and only for callers with financial:view.</param>
/// <param name="Payload">The event's details as recorded (never money).</param>
/// <param name="WorkEntry">For work events, the entry as it is now; null once it was deleted.</param>
/// <param name="IsDemo">Demo data, never to be shown as real history.</param>
public sealed record ActivityItemResponse(
    long Id,
    string Kind,
    ActivityGroup Group,
    string? Domain,
    DateTime OccurredAt,
    ActivityRefResponse? Actor,
    ActivityRefResponse? Project,
    ActivityRefResponse? Client,
    string? SubjectType,
    long? SubjectId,
    decimal? Amount,
    decimal? PrevAmount,
    JsonElement? Payload,
    WorkEntryResponse? WorkEntry,
    bool IsDemo);

public sealed record LoggedHourResponse(int Hour, int Minutes);

/// <summary>Today in Design (company time zone): the work that started today.</summary>
/// <param name="ByLoggedHour">Minutes by the hour of the day they were logged, for hours with any.</param>
public sealed record ActivityTodayResponse(DateOnly Date, int Minutes, int Entries, int People, IReadOnlyList<LoggedHourResponse> ByLoggedHour);
