using ID_Indetails.api.Business.Payroll;

namespace ID_Indetails.api.DTO.Request.Payroll;

public sealed record ProjectPayQuery
{
    public ProjectPayUnit Unit { get; init; } = ProjectPayUnit.Month;

    /// <summary>Any day in the period (the week follows the company's week start); today when empty.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>One Design project only.</summary>
    public long? ProjectId { get; init; }
}
