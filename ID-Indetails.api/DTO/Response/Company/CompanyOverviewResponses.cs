using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.DTO.Response.Company;

/// <summary>One domain over the period. Money is in USD.</summary>
/// <param name="Revenue">The money received in the period, each payment on the day it arrived (see docs/company.md).</param>
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
/// <param name="FixedSalaries">The fixed salaries for the period (by calendar days, like overhead), in USD: the pay of everyone on a
/// pure monthly salary who logs no hours (admins, super admins and Execution's site staff), so no project carries it.</param>
/// <param name="UnconvertedFixedSalaries">Their pay in a currency with no rate for its month, left out of <see cref="FixedSalaries"/>.</param>
/// <param name="PaidLeave">What Design salaries paid for paid leave (paid excused time off) in the period, in USD: nothing is
/// logged for it, so no project carries it.</param>
/// <param name="UnconvertedPaidLeave">Paid leave in a currency with no rate for its month, left out of <see cref="PaidLeave"/>.</param>
/// <param name="AdminSalaries">The phone app's name for <see cref="FixedSalaries"/>, the same figure, until it moves.</param>
/// <param name="UnconvertedAdminSalaries">The phone app's name for <see cref="UnconvertedFixedSalaries"/>, until it moves.</param>
/// <param name="NetProfit">Both domains' profit less company overhead, the fixed salaries and paid leave.</param>
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
    decimal FixedSalaries,
    IReadOnlyList<CurrencyTotalResponse> UnconvertedFixedSalaries,
    decimal PaidLeave,
    IReadOnlyList<CurrencyTotalResponse> UnconvertedPaidLeave,
    decimal AdminSalaries,
    IReadOnlyList<CurrencyTotalResponse> UnconvertedAdminSalaries,
    decimal NetProfit,
    decimal Invoiced,
    decimal Collected,
    decimal Outstanding,
    int ProjectsWithActivity,
    int ClientsServed,
    IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices);

/// <summary>Company overhead charged to a period, as the overview charges it.</summary>
/// <param name="Usd">By calendar days, converted at the rate of each month.</param>
/// <param name="Unconverted">Overhead with no exchange rate for its month, left out of <see cref="Usd"/>.</param>
public sealed record PeriodOverheadResponse(DateOnly From, DateOnly To, decimal Usd, IReadOnlyList<CurrencyTotalResponse> Unconverted);

/// <summary>One month for both domains together, in USD.</summary>
/// <param name="FixedSalaries">The month's fixed salaries (admins, super admins and site staff), a company cost like overhead.</param>
/// <param name="PaidLeave">What Design salaries paid for paid leave in the month, a company cost too.</param>
/// <param name="ProfitBeforeOverhead">Revenue − project costs.</param>
/// <param name="ProfitAfterOverhead">After company overhead only.</param>
/// <param name="NetProfit">After company overhead, the fixed salaries and paid leave, as the overview's net profit.</param>
public sealed record CompanyMonthResponse(
    DateOnly Month,
    decimal Revenue,
    decimal Labour,
    decimal Expenses,
    decimal Materials,
    decimal GroundLabour,
    decimal Overhead,
    decimal FixedSalaries,
    decimal PaidLeave,
    decimal ProfitBeforeOverhead,
    decimal ProfitAfterOverhead,
    decimal NetProfit);
