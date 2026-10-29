using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Boqs;

/// <param name="Number">Null for an internal draft.</param>
/// <param name="TotalUsd">The BOQ's total before supervision and TVA; the work price it adds to once agreed.</param>
/// <param name="Reason">A variation order's reason; null on any other kind.</param>
/// <param name="AgreedBy">Null until agreed.</param>
public sealed record BoqSummaryResponse(
    long Id,
    BoqKind Kind,
    string? Number,
    string Title,
    PersonRefResponse Author,
    BoqStatus Status,
    decimal TotalUsd,
    int LineCount,
    string? Reason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? AgreedAt,
    PersonRefResponse? AgreedBy,
    BoqLastDeliveryResponse? LastDelivery);

public sealed record BoqLastDeliveryResponse(DeliveryChannel Channel, DateTime SentAt);

/// <summary>A BOQ with its lines. Supervision, TVA and the client's total use the project's rates as they are now.</summary>
/// <param name="SupervisionRate">With the supervision, TVA and client total: only for callers who see money or manage the
/// pricing. An internal draft's author may see neither, and the project's rates are pricing.</param>
public sealed record BoqDetailResponse(
    long Id,
    BoqKind Kind,
    string? Number,
    string Title,
    PersonRefResponse Author,
    BoqStatus Status,
    decimal TotalUsd,
    int LineCount,
    string? Reason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? AgreedAt,
    PersonRefResponse? AgreedBy,
    BoqLastDeliveryResponse? LastDelivery,
    long ProjectId,
    BoqAdjustmentType AdjustmentType,
    decimal? AdjustmentValue,
    IReadOnlyList<BoqLineResponse> Lines,
    decimal SubtotalUsd,
    decimal AdjustmentUsd,
    decimal? SupervisionRate,
    decimal? SupervisionUsd,
    decimal? TvaRate,
    decimal? TvaUsd,
    decimal? ClientTotalUsd,
    IReadOnlyList<BoqDeliveryResponse> Deliveries);

/// <param name="Quantity">The exact quantity, before waste. Internal: the client's PDF shows the final quantity.</param>
/// <param name="WasteRate">A fraction. Internal.</param>
/// <param name="FinalQuantity">Quantity plus waste, to 2 decimals.</param>
public sealed record BoqLineResponse(
    long Id,
    int Position,
    string? Code,
    string Description,
    decimal Quantity,
    decimal WasteRate,
    decimal FinalQuantity,
    string? Unit,
    decimal Rate,
    decimal TotalUsd);

/// <param name="TotalUsd">The BOQ's total when it was sent: a draft can still change afterwards.</param>
public sealed record BoqDeliveryResponse(
    long Id,
    DeliveryChannel Channel,
    string? Recipient,
    string? Note,
    decimal TotalUsd,
    DateTime SentAt,
    PersonRefResponse? SentBy);

/// <summary>A project's BOQs, newest first in each list.</summary>
/// <param name="Internal">The caller's own internal drafts; every one for whoever holds <c>project:pricing_manage</c>.</param>
/// <param name="Client">Empty without <c>financial:view</c> or <c>project:pricing_manage</c>.</param>
/// <param name="Vos">Empty without <c>financial:view</c> or <c>project:pricing_manage</c>.</param>
/// <param name="Price">Null without <c>financial:view</c> or <c>project:pricing_manage</c>.</param>
public sealed record BoqListResponse(
    IReadOnlyList<BoqSummaryResponse> Internal,
    IReadOnlyList<BoqSummaryResponse> Client,
    IReadOnlyList<BoqSummaryResponse> Vos,
    BoqPriceResponse? Price);

/// <summary>What the agreed BOQs make the client pay.</summary>
/// <param name="WorkUsd">The agreed client BOQ plus the agreed VOs: the project's work price.</param>
public sealed record BoqPriceResponse(
    decimal AgreedBoqUsd,
    decimal AgreedVosUsd,
    decimal WorkUsd,
    decimal SupervisionRate,
    decimal SupervisionUsd,
    decimal TvaRate,
    decimal TvaUsd,
    decimal ClientTotalUsd);
