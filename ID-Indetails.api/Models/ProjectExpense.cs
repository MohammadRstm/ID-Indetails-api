namespace ID_Indetails.api.Models;

public class ProjectExpense
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Label { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Open vocabulary (services, software, contractor, …), validated by the API.</summary>
    public string Category { get; set; } = "other";

    public DateOnly SpentOn { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Project Project { get; set; } = null!;
}
