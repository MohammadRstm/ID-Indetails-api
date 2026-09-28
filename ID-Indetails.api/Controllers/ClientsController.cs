using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Services.Clients.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Clients and their contacts. Clients of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/clients")]
public sealed class ClientsController(IClientService clientService) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.ClientView)]
    public Task<PagedResponse<ClientSummaryResponse>> List([FromQuery] ClientListQuery query, CancellationToken cancellationToken)
        => clientService.ListAsync(query, cancellationToken);

    [HttpGet("{clientId:long}")]
    [HasPermission(PermissionCodes.ClientView)]
    public Task<ClientResponse> Get(long clientId, CancellationToken cancellationToken)
        => clientService.GetAsync(clientId, cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.ClientCreate)]
    public async Task<ActionResult<ClientResponse>> Create(CreateClientRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await clientService.CreateAsync(request, cancellationToken));

    [HttpPut("{clientId:long}")]
    [HasPermission(PermissionCodes.ClientManage)]
    public Task<ClientResponse> Update(long clientId, UpdateClientRequest request, CancellationToken cancellationToken)
        => clientService.UpdateAsync(clientId, request, cancellationToken);

    [HttpPost("{clientId:long}/archive")]
    [HasPermission(PermissionCodes.ClientManage)]
    public Task<ClientResponse> Archive(long clientId, CancellationToken cancellationToken)
        => clientService.SetArchivedAsync(clientId, true, cancellationToken);

    [HttpPost("{clientId:long}/restore")]
    [HasPermission(PermissionCodes.ClientManage)]
    public Task<ClientResponse> Restore(long clientId, CancellationToken cancellationToken)
        => clientService.SetArchivedAsync(clientId, false, cancellationToken);

    /// <summary>Only while no project uses the client.</summary>
    [HttpDelete("{clientId:long}")]
    [HasPermission(PermissionCodes.ClientManage)]
    public async Task<IActionResult> Delete(long clientId, CancellationToken cancellationToken)
    {
        await clientService.DeleteAsync(clientId, cancellationToken);
        return NoContent();
    }
}
