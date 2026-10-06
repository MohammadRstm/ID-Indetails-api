using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.DTO.Response.Company;

/// <summary>One domain over the period. Money is in USD.</summary>
/// <param name="Revenue">Revenue recognised in the period (see docs/company.md).</param>
/// <param name="Margin">Profit ÷ revenue; null without revenue.</param>
/// <param name="ProjectsWithActivity">Projects with work or costs in the period.</param>
/// <param name="People">Active employees.</param>
/// <param name="Loggers">People who logged work in the period.</param>
/// <param name="TracksHours">Whether anyone in the domain has ever logged hours.</param>
/// <param name="ClientsServed">Clients of projects with activity in the period.</param>
/// <param name="Outstanding">Unpaid on issued invoices today, whatever the period.</param>
/// <param name="UnconvertedCosts">Costs of the period with no exchange rate on their day, left out of <see cref="Cost"/>.</param>
/// <param name="UnconvertedInvoices">Issued invoices with no exchange rate on their issue day, left out of invoiced and outstanding.</param>
public sealed record DomainOverviewResponse(
    string Domain,
    decimal Revenue,
    decimal Cost,
    decimal Profit,
    decimal? Margin,
    int ProjectsWithActivity,
    int ActiveProjects,
    int People,
    int Loggers,
    bool TracksHours,
    int ClientsServed,
    int AllClients,
    decimal Invoiced,
    decimal Collected,
    decimal Outstanding,
    IReadOnlyList<UnconvertedCostResponse> UnconvertedCosts,
    IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices);

/// <param name="Overhead">Company expenses charged to the period (by calendar days), in USD.</param>
/// <param name="UnconvertedOverhead">Overhead with no exchange rate for its month, left out of <see cref="Overhead"/>.</param>
/// <param name="SuperAdminSalaries">The super admins' pay for the period (by calendar days, like overhead), in USD: they log no
/// hours, so no project carries it.</param>
/// <param name="UnconvertedSuperAdminSalaries">Their pay in a currency with no rate for its month, left out of <see cref="SuperAdminSalaries"/>.</param>
/// <param name="NetProfit">Both domains' profit less company overhead and the super admins' salaries.</param>
/// <param name="UnconvertedInvoices">Both domains' invoices with no exchange rate on their issue day.</param>
public sealed record CompanyOverviewResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DomainOverviewResponse> Domains,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit,
    decimal Overhead,
    IReadOnlyList<CurrencyTotalResponse> UnconvertedOverhead,
    decimal SuperAdminSalaries,
    IReadOnlyList<CurrencyTotalResponse> UnconvertedSuperAdminSalaries,
    decimal NetProfit,
    decimal Invoiced,
    decimal Collected,
    decimal Outstanding,
    int ProjectsWithActivity,
    int ClientsServed,
    IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices);

/// <summary>One month for both domains together, in USD.</summary>
/// <param name="SuperAdminSalaries">The super admins' pay for the month, a company cost like overhead.</param>
/// <param name="ProfitBeforeOverhead">Revenue − project costs.</param>
/// <param name="ProfitAfterOverhead">After company overhead only.</param>
/// <param name="NetProfit">After company overhead and the super admins' salaries, as the overview's net profit.</param>
public sealed record CompanyMonthResponse(
    DateOnly Month,
    decimal Revenue,
    decimal Labour,
    decimal Expenses,
    decimal Materials,
    decimal GroundLabour,
    decimal Overhead,
    decimal SuperAdminSalaries,
    decimal ProfitBeforeOverhead,
    decimal ProfitAfterOverhead,
    decimal NetProfit);
