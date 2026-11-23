namespace ID_Indetails.api.DTO.Request.WorkEntries;

/// <param name="OpenUntil">The last day work in the month can still change: today or later.</param>
public sealed record ReopenMonthRequest(DateOnly OpenUntil, string? Note);
