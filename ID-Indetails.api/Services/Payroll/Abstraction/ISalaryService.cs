using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>What people are owed for a month (see <c>Business/Payroll/SalaryCalculator</c>). Always calculated, never stored.</summary>
public interface ISalaryService
{
    Task<SalariesResponse> ListAsync(SalaryListQuery query, CancellationToken cancellationToken = default);

    Task<SalaryDetailResponse> GetAsync(long userId, SalaryMonthQuery query, CancellationToken cancellationToken = default);

    /// <summary>The caller's own month: the same pay as the Salaries screen, with the day-by-day view and estimates.</summary>
    Task<MyPayResponse> GetMineAsync(SalaryMonthQuery query, CancellationToken cancellationToken = default);

    /// <summary>Every row of the filtered list as CSV, with a total per currency.</summary>
    Task<(string FileName, string Csv)> ExportAsync(SalaryListQuery query, CancellationToken cancellationToken = default);
}
