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

public sealed record PaymentMoneyRow(long ProjectId, long ClientId, long? InvoiceId, DateOnly ReceivedOn, decimal AmountUsd, PaymentMethod Method);

public sealed record ExpenseDetailRow(long Id, long ProjectId, string Label, string Category, string Currency, decimal Amount, DateOnly SpentOn);

/// <summary>What the money summaries read besides the shared finance calculator. One query each, whatever the number of projects.</summary>
public interface IMoneySummaryRepository
{
    /// <summary>Every project of the domain, with its client. Not tracked.</summary>
    Task<IReadOnlyList<Project>> DomainProjectsAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Every project of these clients, with its client. Not tracked.</summary>
    Task<IReadOnlyList<Project>> ClientProjectsAsync(IReadOnlyCollection<long> clientIds, CancellationToken cancellationToken = default);

    /// <summary>Every client of the domain, archived ones included. Not tracked.</summary>
    Task<IReadOnlyList<Client>> DomainClientsAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Invoices of these projects, drafts and voided ones included.</summary>
    Task<IReadOnlyList<InvoiceMoneyRow>> InvoicesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Live payments of these projects.</summary>
    Task<IReadOnlyList<PaymentMoneyRow>> PaymentsAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Live expenses of these projects spent between the two days, both inclusive.</summary>
    Task<IReadOnlyList<ExpenseDetailRow>> ExpensesAsync(
        IReadOnlyCollection<long> projectIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
