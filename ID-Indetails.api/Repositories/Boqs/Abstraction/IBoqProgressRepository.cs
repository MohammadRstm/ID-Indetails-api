using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Boqs.Abstraction;

/// <summary>A line of an agreed BOQ or VO of a site, as progress reads it, with what the page shows of it and its BOQ.</summary>
public sealed record BoqProgressLineRow(
    BoqProgressRules.Line Line,
    string? BoqNumber,
    DateTime? AgreedAt,
    int Position,
    string? Code,
    string Description,
    string? Unit);

/// <summary>A record of progress, with the name of who recorded it.</summary>
public sealed record BoqProgressRecordRow(BoqLineProgress Record, string RecordedByName);

public interface IBoqProgressRepository
{
    /// <summary>
    /// The lines of the site's live, agreed client BOQs and VOs, settlement ones included (the rules leave them out): the
    /// client BOQ first, then the VOs in the order they were agreed, each in its lines' order. Not tracked.
    /// </summary>
    Task<IReadOnlyList<BoqProgressLineRow>> AgreedLinesAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Each line's latest record on or before <paramref name="asOf"/>, by line id. Not tracked.</summary>
    Task<IReadOnlyDictionary<long, BoqLineProgress>> LatestAsync(long projectId, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>The latest records of these lines on or before <paramref name="asOf"/>, newest day first, then newest recorded. Not tracked.</summary>
    Task<IReadOnlyList<BoqProgressRecordRow>> HistoryAsync(IReadOnlyCollection<long> lineIds, DateOnly asOf, int take, CancellationToken cancellationToken = default);

    /// <summary>The records of these lines on that day, tracked, to replace.</summary>
    Task<IReadOnlyList<BoqLineProgress>> OnDayForUpdateAsync(IReadOnlyCollection<long> lineIds, DateOnly day, CancellationToken cancellationToken = default);

    void Add(BoqLineProgress record);
}
