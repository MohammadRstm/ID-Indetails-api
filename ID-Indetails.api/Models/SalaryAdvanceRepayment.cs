namespace ID_Indetails.api.Models;

/// <summary>What a salary advance plans to take from the person's pay in one month. A month's rows add up to the advance.</summary>
public class SalaryAdvanceRepayment
{
    public long Id { get; set; }

    public long AdvanceId { get; set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }

    public decimal Amount { get; set; }

    public SalaryAdvance Advance { get; set; } = null!;
}
