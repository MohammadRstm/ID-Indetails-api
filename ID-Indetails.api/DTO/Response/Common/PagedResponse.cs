namespace ID_Indetails.api.DTO.Response.Common;

/// <summary>One page of a list, with the total number of matching items.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
