using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Billing;

public sealed record QuoteDeliveryResponse(long Id, DeliveryChannel Channel, string? Recipient, DateTime SentAt, PersonRefResponse? SentBy, string? Note);

/// <summary>A quote: what the client is quoted (<see cref="TotalUsd"/>), and, for staff, the net amount and TVA behind it.</summary>
/// <param name="Domain">The project's domain, for the client's pages.</param>
/// <param name="NetUsd">Internal: the amount before TVA. The client never sees it.</param>
/// <param name="TvaRate">Internal: the TVA rate in the total.</param>
public sealed record QuoteResponse(
    long Id,
    string Number,
    string Domain,
    long ProjectId,
    string ProjectTitle,
    string? ProjectLocation,
    InvoiceClientResponse Client,
    decimal NetUsd,
    decimal TvaRate,
    decimal TotalUsd,
    string? Scope,
    DateTime CreatedAt,
    PersonRefResponse CreatedBy,
    IReadOnlyList<QuoteDeliveryResponse> Deliveries);
