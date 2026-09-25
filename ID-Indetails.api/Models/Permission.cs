namespace ID_Indetails.api.Models;

/// <summary>A <c>resource:action</c> right. Reference data.</summary>
public class Permission
{
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Null when the permission applies in either domain.</summary>
    public string? DomainCode { get; set; }

    public Domain? Domain { get; set; }
}
