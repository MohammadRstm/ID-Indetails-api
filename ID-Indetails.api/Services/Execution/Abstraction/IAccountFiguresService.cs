using ID_Indetails.api.DTO.Response.Execution;

namespace ID_Indetails.api.Services.Execution.Abstraction;

/// <summary>An Execution site's account: price, payments, costs, supervision and the cash that should be in the project account.</summary>
public interface IAccountFiguresService
{
    /// <summary>Needs <c>financial:view</c>, and an Execution client project in a domain the caller can see.</summary>
    Task<AccountFiguresResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);
}
