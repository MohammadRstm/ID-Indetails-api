namespace ID_Indetails.api.DTO.Request.Expenses;

public sealed record AddExpenseCategoryRequest(string Domain, string Name);

public sealed record RenameExpenseCategoryRequest(string Name);

/// <summary>Every one of the business's categories, in their new order.</summary>
public sealed record OrderExpenseCategoriesRequest(string Domain, IReadOnlyList<long> Ids);
