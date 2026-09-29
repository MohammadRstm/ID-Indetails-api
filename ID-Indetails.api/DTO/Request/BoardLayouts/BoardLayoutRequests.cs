namespace ID_Indetails.api.DTO.Request.BoardLayouts;

public sealed record SaveBoardLayoutRequest
{
    /// <summary>The board's card keys in the order they are shown.</summary>
    public IReadOnlyList<string> Order { get; init; } = [];
}
