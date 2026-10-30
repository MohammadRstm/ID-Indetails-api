using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;

namespace ID_Indetails.api.Services.Company.Abstraction;

public interface IAccountLabelService
{
    /// <summary>The labels that can be picked; archived ones too for someone who manages company settings.</summary>
    Task<IReadOnlyList<AccountLabelResponse>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<AccountLabelResponse> AddAsync(AccountLabelRequest request, CancellationToken cancellationToken = default);

    Task<AccountLabelResponse> RenameAsync(long id, AccountLabelRequest request, CancellationToken cancellationToken = default);

    Task<AccountLabelResponse> ArchiveAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Puts an archived label back, last in the list.</summary>
    Task<AccountLabelResponse> RestoreAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Saves the order of the labels that can be picked; returns every label, archived ones included.</summary>
    Task<IReadOnlyList<AccountLabelResponse>> OrderAsync(AccountLabelOrderRequest request, CancellationToken cancellationToken = default);
}
