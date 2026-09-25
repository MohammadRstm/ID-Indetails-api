namespace ID_Indetails.api.Models;

/// <summary>The craft someone performs on a project (e.g. Architectural, MEP).</summary>
public class Discipline
{
    public long Id { get; set; }

    public string DomainCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Domain Domain { get; set; } = null!;
}
