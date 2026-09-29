using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.BoardLayouts;
using ID_Indetails.api.DTO.Response.BoardLayouts;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.BoardLayouts.Abstraction;
using ID_Indetails.api.Services.BoardLayouts.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;

namespace ID_Indetails.api.Services.BoardLayouts.Implementation;

/// <summary>
/// Only the card order is kept, as in the prototype; the client drops keys it no longer knows and places new cards at
/// their default spot, so the server doesn't check card keys against the boards.
/// </summary>
public sealed class BoardLayoutService(ICurrentUser currentUser, IBoardLayoutRepository layouts, IClock clock) : IBoardLayoutService
{
    private sealed record StoredLayout(IReadOnlyList<string> Order);

    public async Task<BoardLayoutResponse> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        CheckKey(key);
        var saved = await layouts.GetAsync(currentUser.Id, key, cancellationToken);
        return saved is null
            ? new BoardLayoutResponse(key, null, null)
            : new BoardLayoutResponse(key, JsonSerializer.Deserialize<StoredLayout>(saved.Layout, JsonSerializerOptions.Web)?.Order, saved.SavedAt);
    }

    public async Task<BoardLayoutResponse> SaveAsync(string key, SaveBoardLayoutRequest request, CancellationToken cancellationToken = default)
    {
        CheckKey(key);
        var now = clock.UtcNow;
        await layouts.SaveAsync(
            currentUser.Id, key, JsonSerializer.Serialize(new StoredLayout(request.Order), JsonSerializerOptions.Web), now, cancellationToken);
        return new BoardLayoutResponse(key, request.Order, now);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        CheckKey(key);
        await layouts.DeleteAsync(currentUser.Id, key, cancellationToken);
    }

    private static void CheckKey(string key)
    {
        if (!BoardKeys.All.Contains(key))
        {
            throw new NotFoundException("Board not found.");
        }
    }
}
