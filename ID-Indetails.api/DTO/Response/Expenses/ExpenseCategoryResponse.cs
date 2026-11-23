namespace ID_Indetails.api.DTO.Response.Expenses;

/// <param name="Code">What expenses store: fixed once made.</param>
/// <param name="Removable">Whether it can be removed: not "Other", and nothing uses it (only given to whoever manages them).</param>
public sealed record ExpenseCategoryResponse(long Id, string Domain, string Code, string Name, int SortOrder, bool? Removable = null);
