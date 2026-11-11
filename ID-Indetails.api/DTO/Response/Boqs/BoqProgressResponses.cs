using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Boqs;

/// <summary>
/// A site's progress by value: each work line of its agreed client BOQ and agreed VOs with what was executed on it, and
/// the value executed against the agreed value. Quantities and progress are for whoever reads the site's BOQs or oversees
/// it; the prices and values only for <c>financial:view</c>, and null (never 0) for anyone else.
/// </summary>
/// <param name="AsOf">The company day the executed quantities stand at.</param>
/// <param name="AgreedValue">The counted lines' agreed value: their work, net of TVA, without supervision; null without financial:view.</param>
/// <param name="ValueExecuted">Each line's executed quantity × its unit price, added up; null without financial:view.</param>
/// <param name="Progress">Value executed ÷ agreed value, a fraction to four places (may pass 1); null until a line has a record, or while the agreed value is 0.</param>
/// <param name="History">The latest records up to <paramref name="AsOf"/>, newest first (at most 50).</param>
public sealed record BoqProgressResponse(
    long ProjectId,
    DateOnly AsOf,
    IReadOnlyList<BoqProgressLineResponse> Lines,
    decimal? AgreedValue,
    decimal? ValueExecuted,
    decimal? Progress,
    IReadOnlyList<BoqProgressRecordResponse> History);

/// <summary>A work line of an agreed client BOQ or VO, and what was executed on it.</summary>
/// <param name="Quantity">The line's final quantity (with waste), in its unit: what progress is measured against.</param>
/// <param name="TakesProgress">False on a line that takes work away (a negative rate): nothing on it is executed, and it only lowers the agreed value.</param>
/// <param name="ExecutedQuantity">The latest quantity recorded on or before the day; null when none was.</param>
/// <param name="ExecutedOn">The day of that record.</param>
/// <param name="Progress">Executed ÷ quantity, a fraction to four places (may pass 1).</param>
/// <param name="UnitPrice">The rate with its BOQ's adjustment spread over it, net of TVA; null without financial:view.</param>
/// <param name="AgreedValue">Quantity × unit price; null without financial:view.</param>
/// <param name="ValueExecuted">Executed × unit price (0 without a record); null without financial:view.</param>
public sealed record BoqProgressLineResponse(
    long BoqLineId,
    long BoqId,
    BoqKind BoqKind,
    string? BoqNumber,
    string BoqTitle,
    int Position,
    string? Code,
    string Description,
    string? Unit,
    decimal Quantity,
    bool TakesProgress,
    decimal? ExecutedQuantity,
    DateOnly? ExecutedOn,
    decimal? Progress,
    decimal? UnitPrice,
    decimal? AgreedValue,
    decimal? ValueExecuted);

/// <summary>One record: a line's executed quantity as it stood on a day, and who recorded it.</summary>
public sealed record BoqProgressRecordResponse(
    long Id,
    long BoqLineId,
    DateOnly RecordedOn,
    decimal ExecutedQuantity,
    string? Note,
    PersonRefResponse RecordedBy,
    DateTime RecordedAt);
