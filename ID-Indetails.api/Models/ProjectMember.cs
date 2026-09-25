namespace ID_Indetails.api.Models;

public class ProjectMember
{
    public long ProjectId { get; set; }

    public long UserId { get; set; }

    public long? DisciplineId { get; set; }

    public DateTime AddedAt { get; set; }

    public long? AddedById { get; set; }

    public Project Project { get; set; } = null!;

    public User User { get; set; } = null!;

    public Discipline? Discipline { get; set; }
}
