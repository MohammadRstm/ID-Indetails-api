namespace ID_Indetails.api.Models;

/// <summary>A department inside a domain (e.g. Structural, Site Operations). Drives group chats.</summary>
public class Team
{
    public long Id { get; set; }

    public string DomainCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Domain Domain { get; set; } = null!;
}
