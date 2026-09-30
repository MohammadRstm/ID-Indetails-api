using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;

namespace ID_Indetails.api.Services.Clients.Abstraction;

public interface IClientService
{
    Task<PagedResponse<ClientSummaryResponse>> ListAsync(ClientListQuery query, CancellationToken cancellationToken = default);

    /// <summary>The client list's header figures and 13-month growth.</summary>
    Task<ClientOverviewResponse> OverviewAsync(ClientOverviewQuery query, CancellationToken cancellationToken = default);

    Task<ClientResponse> GetAsync(long clientId, CancellationToken cancellationToken = default);

    Task<ClientResponse> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default);

    Task<ClientResponse> UpdateAsync(long clientId, UpdateClientRequest request, CancellationToken cancellationToken = default);

    Task<ClientResponse> SetArchivedAsync(long clientId, bool archived, CancellationToken cancellationToken = default);

    /// <summary>Only a client no project uses.</summary>
    Task DeleteAsync(long clientId, CancellationToken cancellationToken = default);
}
