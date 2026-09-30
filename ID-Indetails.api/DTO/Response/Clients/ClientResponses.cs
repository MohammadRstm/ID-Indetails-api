using System.Text.Json.Serialization;
using ID_Indetails.api.DTO.Response.Finance;
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

    /// <summary>Only for callers with financial:view; left out entirely otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientMoneyResponse? Money { get; init; }

    /// <summary>Only for callers with work_entry:view_all; left out entirely otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientListWorkResponse? Work { get; init; }
}

/// <summary>The work logged on a client's projects (Design logs work; Execution doesn't).</summary>
public sealed record ClientListWorkResponse(int TotalMinutes, DateTime? LastStartedAt);

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

    /// <summary>Only for callers with financial:view; left out entirely otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientMoneyResponse? Money { get; init; }

    /// <summary>Each of the client's projects with its money, newest first. Only for callers with financial:view.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ClientProjectMoneyResponse>? ProjectMoney { get; init; }

    /// <summary>The work logged for the client. Only for callers with work_entry:view_all.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientWorkResponse? Work { get; init; }
}

public sealed record MonthMinutesResponse(DateOnly Month, int Minutes);

public sealed record ClientPersonWorkResponse(long Id, string Name, int Minutes);

/// <summary>The work logged on a client's projects, all time (Design logs work; Execution doesn't).</summary>
/// <param name="Monthly">The last 12 months, ending with this one, oldest first.</param>
/// <param name="People">The 8 people who logged the most time for the client.</param>
/// <param name="ContributorCount">Everyone who logged work for the client.</param>
public sealed record ClientWorkResponse(
    int TotalMinutes,
    int EntryCount,
    int ContributorCount,
    IReadOnlyList<MonthMinutesResponse> Monthly,
    IReadOnlyList<ClientPersonWorkResponse> People);

public sealed record ClientAddedResponse(long Id, string Name, DateTime AddedAt);

/// <param name="Month">First day of the month.</param>
/// <param name="Total">Clients on the books at the end of the month, archived ones included.</param>
public sealed record ClientGrowthMonthResponse(DateOnly Month, int Total, IReadOnlyList<ClientAddedResponse> Added);

/// <summary>
/// The client list's header figures and growth chart. Its money figures (revenue, profit, margin, the largest
/// client) come from <c>GET /api/finance/{domain}/clients</c>, for callers with financial:view.
/// </summary>
/// <param name="Clients">Clients not archived.</param>
/// <param name="WithActiveWork">With an active or on-hold project.</param>
/// <param name="AddedThisMonth">Clients not archived that were added this month.</param>
/// <param name="Newest">The client added last, archived or not.</param>
public sealed record ClientOverviewResponse(
    int Clients,
    int Archived,
    int WithActiveWork,
    int WithoutProjects,
    int AddedThisMonth,
    ClientAddedResponse? Newest,
    IReadOnlyList<ClientGrowthMonthResponse> Growth);
