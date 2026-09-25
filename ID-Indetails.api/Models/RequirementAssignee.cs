namespace ID_Indetails.api.Models;

public class RequirementAssignee
{
    public long RequirementId { get; set; }

    public long UserId { get; set; }

    public ProjectRequirement Requirement { get; set; } = null!;

    public User User { get; set; } = null!;
}
