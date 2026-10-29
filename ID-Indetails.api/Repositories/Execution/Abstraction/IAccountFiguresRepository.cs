using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Repositories.Execution.Abstraction;

/// <summary>A live payment: what it was worth in USD (TVA included) and the label it is filed under.</summary>
public sealed record AccountPaymentRow(long? AccountLabelId, decimal AmountUsd);

/// <summary>A site expense as recorded: in its own currency, on the day it was spent.</summary>
public sealed record AccountExpenseRow(
    long? AccountLabelId,
    SiteExpenseCategory Category,
    string? Supplier,
    string Currency,
    decimal Total,
    DateOnly SpentOn);

/// <summary>What an Execution site's account figures split by label, category and payee, which its shared money doesn't carry.</summary>
public interface IAccountFiguresRepository
{
    /// <summary>The project's payments that were not voided.</summary>
    Task<IReadOnlyList<AccountPaymentRow>> LivePaymentsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The project's site expenses that were not removed, the latest recorded first.</summary>
    Task<IReadOnlyList<AccountExpenseRow>> SiteExpensesAsync(long projectId, CancellationToken cancellationToken = default);
}
