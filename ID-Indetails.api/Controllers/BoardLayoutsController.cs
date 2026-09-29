using ID_Indetails.api.DTO.Request.BoardLayouts;
using ID_Indetails.api.DTO.Response.BoardLayouts;
using ID_Indetails.api.Services.BoardLayouts.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The caller's own dashboard card orders. Signed in only; nobody reads or changes anyone else's.</summary>
[ApiController]
[Route("api/me/board-layouts")]
[Authorize]
public sealed class BoardLayoutsController(IBoardLayoutService layouts) : ControllerBase
{
    [HttpGet("{key}")]
    public Task<BoardLayoutResponse> Get(string key, CancellationToken cancellationToken)
        => layouts.GetAsync(key, cancellationToken);

    /// <summary>Creates or replaces the board's saved order.</summary>
    [HttpPut("{key}")]
    public Task<BoardLayoutResponse> Save(string key, SaveBoardLayoutRequest request, CancellationToken cancellationToken)
        => layouts.SaveAsync(key, request, cancellationToken);

    /// <summary>Goes back to the board's default order.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken cancellationToken)
    {
        await layouts.DeleteAsync(key, cancellationToken);
        return NoContent();
    }
}
