using ID_Indetails.api.DTO.Response.Common;

namespace ID_Indetails.api.DTO.Response.WorkEntries;

/// <summary>A closed month open for logging again. <paramref name="Month"/> is <c>yyyy-MM</c>.</summary>
public sealed record ReopenedMonthResponse(string Month, DateOnly OpenUntil, string Note, PersonRefResponse ReopenedBy, DateTime ReopenedAt);
