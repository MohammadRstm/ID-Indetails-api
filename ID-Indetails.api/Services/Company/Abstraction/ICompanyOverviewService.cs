using ID_Indetails.api.DTO.Response.Company;

namespace ID_Indetails.api.Services.Company.Abstraction;

/// <summary>Both domains and the company overhead together (see docs/company.md). Always calculated.</summary>
public interface ICompanyOverviewService
{
    /// <param name="from">First day; the first of <paramref name="to"/>'s month when empty.</param>
    /// <param name="to">Last day; today when empty, and never after today.</param>
    Task<CompanyOverviewResponse> GetAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>The overhead <see cref="GetAsync"/> charges to the same period, without the rest of the overview.</summary>
    Task<PeriodOverheadResponse> OverheadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The last <paramref name="months"/> months, this one included.</summary>
    Task<IReadOnlyList<CompanyMonthResponse>> MonthlyAsync(int months, CancellationToken cancellationToken = default);

    /// <summary>The months of a year, January first, up to this month in the current year. Never a future year.</summary>
    Task<IReadOnlyList<CompanyMonthResponse>> YearAsync(int year, CancellationToken cancellationToken = default);
}
