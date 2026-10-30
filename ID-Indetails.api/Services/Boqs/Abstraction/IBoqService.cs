using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Boqs.Abstraction;

/// <summary>A client BOQ or VO with what its PDF and email show beside it: the project and its client.</summary>
public sealed record BoqDocument(BoqDetailResponse Boq, string ProjectTitle, string? ProjectLocation, string ClientName);

/// <summary>
/// Bills of quantities of Execution client projects in the caller's visible domains: internal drafts, the client BOQ and
/// variation orders. Agreeing or withdrawing one is the only way a site's work price changes.
/// </summary>
public interface IBoqService
{
    Task<BoqListResponse> ListAsync(long projectId, CancellationToken cancellationToken = default);

    Task<BoqDetailResponse> GetAsync(long boqId, CancellationToken cancellationToken = default);

    Task<BoqDetailResponse> CreateAsync(long projectId, CreateBoqRequest request, CancellationToken cancellationToken = default);

    /// <summary>Saves the whole document: its lines are replaced in the order given, and every total is worked out again.</summary>
    Task<BoqDetailResponse> SaveAsync(long boqId, SaveBoqRequest request, CancellationToken cancellationToken = default);

    Task<BoqDetailResponse> CopyLinesAsync(long boqId, CopyBoqLinesRequest request, CancellationToken cancellationToken = default);

    Task<BoqDetailResponse> DuplicateAsync(long boqId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a draft. What was sent stays on record.</summary>
    Task DeleteAsync(long boqId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the BOQ and sets the project's work price to the agreed client BOQ plus the agreed VOs. A change that would
    /// take the project's profit to zero or below is refused and kept for forcing.
    /// </summary>
    Task<BoqDetailResponse> AgreeAsync(long boqId, CancellationToken cancellationToken = default);

    /// <summary>Returns the BOQ to draft and takes it out of the work price, with the same profit floor.</summary>
    Task<BoqDetailResponse> WithdrawAgreementAsync(long boqId, WithdrawBoqAgreementRequest request, CancellationToken cancellationToken = default);

    Task<BoqDetailResponse> RecordDeliveryAsync(long boqId, RecordBoqDeliveryRequest request, CancellationToken cancellationToken = default);

    /// <summary>A client BOQ or VO for its PDF. An internal draft has none (<c>boq_internal</c>).</summary>
    Task<BoqDocument> GetDocumentAsync(long boqId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="GetDocumentAsync"/>, for sending: refused unless the caller may send it now, so an email never
    /// goes out that can't be recorded.
    /// </summary>
    Task<BoqDocument> GetDocumentForSendingAsync(long boqId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completing a site whose client paid beyond its price: an agreed VO (a client BOQ when none was agreed) that raises
    /// the work price to <paramref name="work"/>. Runs inside the caller's transaction, on the project it has locked.
    /// </summary>
    Task AddClosingPriceAsync(Project project, decimal work, long actorId, CancellationToken cancellationToken = default);
}
