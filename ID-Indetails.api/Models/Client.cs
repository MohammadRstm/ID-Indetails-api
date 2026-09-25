namespace ID_Indetails.api.Models;

/// <summary>
/// A customer of one domain. Clients never sign in. A client with projects can only be
/// archived, never deleted, so project history keeps its client.
/// </summary>
public class Client
{
    public long Id { get; set; }

    public string DomainCode { get; set; } = string.Empty;

    /// <summary>Contact person.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Organisation { get; set; }

    /// <summary>Free label such as Client, Key account, Retainer.</summary>
    public string? Label { get; set; }

    public string? Note { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Domain Domain { get; set; } = null!;

    public ICollection<ClientContact> Contacts { get; set; } = new List<ClientContact>();

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
