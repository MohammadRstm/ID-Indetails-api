namespace ID_Indetails.api.DTO.Response.BoardLayouts;

/// <param name="Order">The saved card order; null while nothing is saved, so the board shows its default.</param>
public sealed record BoardLayoutResponse(string Key, IReadOnlyList<string>? Order, DateTime? SavedAt);
