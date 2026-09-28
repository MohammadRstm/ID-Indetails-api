using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Clients;

public sealed record ClientContactResponse(long Id, ContactKind Kind, string Value, string? Label, bool IsWhatsapp, bool IsPrimary);

public sealed record ClientSummaryResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Organisation { get; init; }

    /// <summary>The organisation when there is one, otherwise the person's name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    public string? Label { get; init; }

    public bool IsArchived { get; init; }

    public ClientContactResponse? PrimaryContact { get; init; }

    public int ProjectCount { get; init; }
}

public sealed record ClientResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Organisation { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Label { get; init; }

    public string? Note { get; init; }

    public bool IsArchived { get; init; }

    public DateTime CreatedAt { get; init; }

    public IReadOnlyList<ClientContactResponse> Contacts { get; init; } = [];

    public int ProjectCount { get; init; }

    /// <summary>A warning only: another client of the domain has the same name. Saving is still allowed.</summary>
    public bool NameSharedWithAnotherClient { get; init; }
}
