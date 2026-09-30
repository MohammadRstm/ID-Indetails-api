using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Clients;

/// <summary>Filters for the client list. Everything is optional.</summary>
public sealed record ClientListQuery
{
    /// <summary>Matches name, organisation, label, note and contact details; every word must match.</summary>
    public string? Search { get; init; }

    /// <summary>True for archived clients only. When empty, archived clients are left out.</summary>
    public bool? Archived { get; init; }

    /// <summary>Archived clients alongside the others (the prototype's "Show archived"). Not with <see cref="Archived"/>.</summary>
    public bool IncludeArchived { get; init; }

    /// <summary>Must be a domain the caller can see, or nothing is returned.</summary>
    public string? Domain { get; init; }

    public ClientFilter? Filter { get; init; }

    public ClientSort Sort { get; init; } = ClientSort.Name;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>What the client form has typed so far, to warn about duplicates before saving.</summary>
public sealed record CheckClientRequest
{
    /// <summary>Needed only by callers who see both domains.</summary>
    public string? DomainCode { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<CheckClientContactRequest> Contacts { get; init; } = [];

    /// <summary>The client being edited, which may of course keep its own name and contacts.</summary>
    public long? ExceptClientId { get; init; }
}

public sealed record CheckClientContactRequest
{
    public ContactKind Kind { get; init; }

    public string Value { get; init; } = string.Empty;
}

public sealed record ClientOverviewQuery
{
    /// <summary>Must be a domain the caller can see; both of them when empty.</summary>
    public string? Domain { get; init; }
}

public sealed record ClientContactRequest
{
    public ContactKind Kind { get; init; }

    public string Value { get; init; } = string.Empty;

    public string? Label { get; init; }

    /// <summary>Phones only.</summary>
    public bool IsWhatsapp { get; init; }

    /// <summary>At most one. When none is marked, the first contact is primary.</summary>
    public bool IsPrimary { get; init; }
}

public sealed record CreateClientRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Organisation { get; init; }

    public string Label { get; init; } = string.Empty;

    public string? Note { get; init; }

    /// <summary>May be empty when the caller works in only one domain.</summary>
    public string? DomainCode { get; init; }

    public IReadOnlyList<ClientContactRequest> Contacts { get; init; } = [];
}

/// <summary>Replaces every field and the whole contact list.</summary>
public sealed record UpdateClientRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Organisation { get; init; }

    public string Label { get; init; } = string.Empty;

    public string? Note { get; init; }

    public IReadOnlyList<ClientContactRequest> Contacts { get; init; } = [];
}
