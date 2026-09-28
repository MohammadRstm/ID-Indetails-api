using ID_Indetails.api.Models;

namespace ID_Indetails.api.Constants;

/// <summary>
/// The starting teams of each domain, taken from the prototype. Seeded by the
/// TeamsAndTeamPermission migration; after that, teams are managed through the API
/// (<c>team:manage</c>). Changing this list changes seed data, so it needs approval.
/// </summary>
public static class TeamSeed
{
    private static readonly DateTime SeededAt = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyList<Team> Teams =
    [
        New(1, DomainCodes.Design, "Engineering"),
        New(2, DomainCodes.Design, "Design"),
        New(3, DomainCodes.Design, "Data"),
        New(4, DomainCodes.Design, "Marketing"),
        New(5, DomainCodes.Design, "Operations"),
        New(6, DomainCodes.Design, "Platform"),
        New(7, DomainCodes.Execution, "Site Operations"),
        New(8, DomainCodes.Execution, "Procurement"),
        New(9, DomainCodes.Execution, "Quantity Surveying"),
        New(10, DomainCodes.Execution, "Safety"),
        New(11, DomainCodes.Execution, "Project Management"),
    ];

    private static Team New(long id, string domain, string name)
        => new() { Id = id, DomainCode = domain, Name = name, CreatedAt = SeededAt };
}
