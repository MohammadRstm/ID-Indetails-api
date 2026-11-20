namespace ID_Indetails.api.DTO.Response.DevSignIn;

/// <summary>Development only: the accounts the sign-in page's picker offers, in the order it shows them.</summary>
public sealed record DevAccountsResponse
{
    public IReadOnlyList<DevAccountGroupResponse> Groups { get; init; } = [];
}

/// <summary>One kind of account, such as "Design employees" or "Clients (portal)".</summary>
public sealed record DevAccountGroupResponse
{
    public string Label { get; init; } = string.Empty;

    public IReadOnlyList<DevAccountResponse> Accounts { get; init; } = [];
}

public sealed record DevAccountResponse
{
    /// <summary>Staff sign in to the app; clients to the portal.</summary>
    public DevAccountKind Kind { get; init; }

    /// <summary>The user's id for staff, the portal account's id for clients.</summary>
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    /// <summary>A staff member's position, or else their team; null when they have neither, and for clients.</summary>
    public string? Detail { get; init; }
}

public enum DevAccountKind
{
    Staff,
    Client,
}
