using ID_Indetails.api.DTO.Request.BoardLayouts;
using ID_Indetails.api.DTO.Response.BoardLayouts;

namespace ID_Indetails.api.Services.BoardLayouts.Abstraction;

/// <summary>The caller's own saved card orders, one per board. A board key that doesn't exist is not found.</summary>
public interface IBoardLayoutService
{
    Task<BoardLayoutResponse> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<BoardLayoutResponse> SaveAsync(string key, SaveBoardLayoutRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
