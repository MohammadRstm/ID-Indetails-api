using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Company.Abstraction;

/// <summary>Company-wide figures for the overview, across both domains, each in one query.</summary>
public interface ICompanyFinanceRepository
{
    /// <summary>Every project of both domains, not tracked.</summary>
    Task<IReadOnlyList<Project>> ProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Active employees per domain.</summary>
    Task<IReadOnlyDictionary<string, int>> EmployeesAsync(CancellationToken cancellationToken = default);

    /// <summary>Clients per domain.</summary>
    Task<IReadOnlyDictionary<string, int>> ClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>Issued invoices that aren't void, with what their live payments add up to (USD).</summary>
    Task<IReadOnlyList<IssuedInvoiceRow>> IssuedInvoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Live payments.</summary>
    Task<IReadOnlyList<ReceivedPaymentRow>> PaymentsAsync(CancellationToken cancellationToken = default);
}

public sealed record IssuedInvoiceRow(string Domain, DateTime IssuedAt, decimal Total, string Currency, decimal PaidUsd);

public sealed record ReceivedPaymentRow(string Domain, DateOnly ReceivedOn, decimal AmountUsd);
