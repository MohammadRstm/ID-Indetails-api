namespace ID_Indetails.api.DTO.Request.Boqs;

/// <summary>
/// Executed quantities on some of a site's agreed BOQ lines, as they stand on a day. A line already recorded that day is
/// replaced; the lines left out keep theirs.
/// </summary>
public sealed record RecordBoqProgressRequest
{
    /// <summary>The company day the quantities stand at: today when left out, never a day still to come.</summary>
    public DateOnly? RecordedOn { get; init; }

    /// <summary>1 to 200 lines, each once.</summary>
    public IReadOnlyList<BoqProgressLineRequest> Lines { get; init; } = [];
}

/// <param name="BoqLineId">A line of the site's agreed client BOQ or of an agreed VO.</param>
/// <param name="ExecutedQuantity">
/// Everything executed on the line up to the day, in its unit (not that day's work alone): 0 up to twice the line's final
/// quantity, to four places.
/// </param>
/// <param name="Note">Up to 500 characters.</param>
public sealed record BoqProgressLineRequest(long BoqLineId, decimal ExecutedQuantity, string? Note = null);

/// <summary>A site's progress as it stood on a day.</summary>
public sealed record BoqProgressQuery
{
    /// <summary>The company day to read the executed quantities at (the latest record on or before it): today when left out.</summary>
    public DateOnly? AsOf { get; init; }
}
