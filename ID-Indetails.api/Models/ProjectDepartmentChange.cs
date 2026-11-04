using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A change of a project's departments or of their shares, with the split before and after (JSON lists of
/// <c>{ teamId, name, share }</c>, names as they were then) and why.
/// </summary>
public class ProjectDepartmentChange
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public ProjectDepartmentChangeKind Kind { get; set; }

    /// <summary>Null when the project was created.</summary>
    public string? Before { get; set; }

    public string After { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public long ChangedById { get; set; }

    public DateTime ChangedAt { get; set; }

    public Project Project { get; set; } = null!;
}
