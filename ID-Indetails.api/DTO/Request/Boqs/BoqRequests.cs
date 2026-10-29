using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Boqs;

/// <summary>A new BOQ of a project. Internal drafts need <c>boq:create</c>; client BOQs and VOs need <c>project:pricing_manage</c>.</summary>
public sealed record CreateBoqRequest
{
    public BoqKind Kind { get; init; }

    /// <summary>Defaults to "Client BOQ", "Internal draft" or "Variation order".</summary>
    public string? Title { get; init; }

    /// <summary>A variation order's reason; required there, refused on any other kind.</summary>
    public string? Reason { get; init; }

    /// <summary>Starts the BOQ with all the lines of this one, which the caller can see and which belongs to the same project.</summary>
    public long? FromBoqId { get; init; }
}

/// <summary>The whole document: lines are replaced in the order given. The server works out every total.</summary>
public sealed record SaveBoqRequest
{
    public string Title { get; init; } = string.Empty;

    /// <summary>A variation order's reason; required there, refused on any other kind.</summary>
    public string? Reason { get; init; }

    public BoqAdjustmentType AdjustmentType { get; init; }

    /// <summary><c>percent</c>: a fraction (-0.05 takes 5% off). <c>total</c>: the BOQ's new total. Absent for none.</summary>
    public decimal? AdjustmentValue { get; init; }

    public IReadOnlyList<SaveBoqLineRequest> Lines { get; init; } = [];
}

public sealed record SaveBoqLineRequest
{
    /// <summary>The id of one of this BOQ's lines, to keep it; absent for a new line.</summary>
    public long? Id { get; init; }

    /// <summary>The line's number in the BOQ, e.g. "2.1.1".</summary>
    public string? Code { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>The exact quantity, before waste.</summary>
    public decimal Quantity { get; init; }

    /// <summary>A fraction: 0.05 adds 5% to the quantity. Defaults to none.</summary>
    public decimal WasteRate { get; init; }

    public string? Unit { get; init; }

    /// <summary>Negative only on a variation order.</summary>
    public decimal Rate { get; init; }
}

public sealed record CopyBoqLinesRequest
{
    public long FromBoqId { get; init; }

    /// <summary>The lines to copy, in the source's order; absent copies them all.</summary>
    public IReadOnlyList<long>? LineIds { get; init; }
}

public sealed record WithdrawBoqAgreementRequest
{
    public string Reason { get; init; } = string.Empty;
}

/// <summary>Emails a client BOQ or VO to the client with its PDF attached.</summary>
public sealed record EmailBoqRequest
{
    public string To { get; init; } = string.Empty;

    /// <summary>Kept with the delivery; not in the email.</summary>
    public string? Note { get; init; }
}

public sealed record RecordBoqDeliveryRequest
{
    public DeliveryChannel Channel { get; init; }

    public string? Recipient { get; init; }

    public string? Note { get; init; }
}
