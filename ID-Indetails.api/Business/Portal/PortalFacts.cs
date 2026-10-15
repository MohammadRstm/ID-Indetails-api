using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>A project as the portal's invitation rules see it, with its client's email contacts.</summary>
public sealed record PortalProjectFacts(
    long Id,
    string DomainCode,
    string Title,
    ProjectStatus Status,
    BillingType BillingType,
    long? ClientId,
    bool ClientArchived,
    IReadOnlyList<PortalClientContact> EmailContacts);

/// <summary>One email contact of the project's client: its normalised address and its label.</summary>
public sealed record PortalClientContact(string Email, string? Label);

/// <summary>
/// A project an account may open in the portal, with what its card in the project list needs: when the client last
/// opened it, its last publication, its newest message and comment (deleted ones aside), and the file of its first
/// published hero image.
/// </summary>
public sealed record PortalProjectSummary(
    long Id,
    string Title,
    ProjectStatus Status,
    DateOnly StartedOn,
    DateTime? LastOpenedAt,
    DateTime? PublishedAt,
    DateTime? LastMessageAt,
    DateTime? LastCommentAt,
    long? HeroFileId);
