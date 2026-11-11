using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.MoneySummaries.Abstraction;

/// <param name="PaidUsd">Live payments against it.</param>
public sealed record InvoiceMoneyRow(
    long Id,
    long ProjectId,
    long ClientId,
    string? Number,
    string Currency,
    decimal Total,
    DateTime? IssuedAt,
    DateTime? DueAt,
    bool IsVoided,
    decimal PaidUsd);

public sealed record PaymentMoneyRow(long ProjectId, long ClientId, long? InvoiceId, DateOnly ReceivedOn, decimal AmountUsd, PaymentMethod Method, decimal TvaUsd);

/// <summary>
/// A project's agreed payment (Design), or its work price or permits (Execution, by <paramref name="Kind"/>), being set or
/// changed: the new amount, and the one before.
/// </summary>
public sealed record AgreedChangeRow(long ProjectId, DateTime OccurredAt, decimal Amount, decimal? PrevAmount, string Kind);

public sealed record StatusCountRow(ProjectStatus Status, int Count);

public sealed record RunningProjectRow(long Id, string Title, DateOnly StartDate);

public sealed record ExpenseDetailRow(long Id, long ProjectId, string Label, string Category, string Currency, decimal Amount, DateOnly SpentOn);

/// <summary>A site expense with its item: <paramref name="Quantity"/> and <paramref name="UnitCost"/> when it was entered as a quantity × a unit cost.</summary>
/// <param name="Total">Its cost in its own currency, whichever way it was entered.</param>
public sealed record SiteExpenseDetailRow(
    long Id,
    long ProjectId,
    string Name,
    SiteExpenseCategory Category,
    decimal? Quantity,
    string? Unit,
    decimal? UnitCost,
    decimal Total,
    string Currency,
    DateOnly SpentOn);

/// <summary>What the money summaries read besides the shared finance calculator. One query each, whatever the number of projects.</summary>
public interface IMoneySummaryRepository
{
    /// <summary>Every project of the domain, with its client. Not tracked.</summary>
    Task<IReadOnlyList<Project>> DomainProjectsAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Every project of these clients, with its client. Not tracked.</summary>
    Task<IReadOnlyList<Project>> ClientProjectsAsync(IReadOnlyCollection<long> clientIds, CancellationToken cancellationToken = default);

    /// <summary>How many of the domain's projects are in each status.</summary>
    Task<IReadOnlyList<StatusCountRow>> StatusCountsAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>The domain's active and on-hold projects that started first, oldest first.</summary>
    Task<IReadOnlyList<RunningProjectRow>> LongestRunningAsync(string domainCode, int count, CancellationToken cancellationToken = default);

    /// <summary>How many of the domain's clients aren't archived.</summary>
    Task<int> ActiveClientCountAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Every client of the domain, archived ones included. Not tracked.</summary>
    Task<IReadOnlyList<Client>> DomainClientsAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Invoices of these projects, drafts and voided ones included.</summary>
    Task<IReadOnlyList<InvoiceMoneyRow>> InvoicesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Every time these projects' agreed payment or fee was set or changed, from the activity log.</summary>
    Task<IReadOnlyList<AgreedChangeRow>> AgreedChangesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Live payments of these projects.</summary>
    Task<IReadOnlyList<PaymentMoneyRow>> PaymentsAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Live expenses of these projects spent between the two days, both inclusive.</summary>
    Task<IReadOnlyList<ExpenseDetailRow>> ExpensesAsync(
        IReadOnlyCollection<long> projectIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Live site expenses of these projects spent between the two days, both inclusive, with their items.</summary>
    Task<IReadOnlyList<SiteExpenseDetailRow>> SiteExpensesAsync(
        IReadOnlyCollection<long> projectIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
