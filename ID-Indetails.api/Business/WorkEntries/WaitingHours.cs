using System.Text.Json;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Business.WorkEntries;

/// <summary>
/// Hours logged on a project the logger's department isn't on: kept as a blocked change until approved or turned down.
/// Until then they hold their time on the logger's day, as if logged: nothing else is logged over them.
/// </summary>
public static class WaitingHours
{
    /// <summary>The log as it was sent; null when the kept change isn't one.</summary>
    public static SaveWorkEntryRequest? Of(BlockedChange change, JsonSerializerOptions json)
        => JsonSerializer.Deserialize<BlockedRequest<SaveWorkEntryRequest>>(change.Payload, json)?.Request;

    /// <summary>The waiting hours as spans for the overlap check, their ids below zero so they never stand for an entry.</summary>
    public static IReadOnlyList<WorkEntryRules.Span> Spans(
        IEnumerable<(BlockedChange Change, string ProjectTitle)> waiting, JsonSerializerOptions json, DateTime from, DateTime to)
        => waiting
            .Select(w => (w, Request: Of(w.Change, json)))
            .Where(x => x.Request is not null)
            .Select(x => new WorkEntryRules.Span(
                -x.w.Change.Id, WorkEntryRules.AsUtc(x.Request!.StartedAt), WorkEntryRules.AsUtc(x.Request.EndedAt), $"{x.w.ProjectTitle} (waiting for approval)"))
            .Where(s => s.Start < to && s.End > from)
            .ToList();
}
