using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Employees.Abstraction;

/// <summary>Read side of people management. Changes go through tracked users from <c>IUserRepository</c>.</summary>
public interface IEmployeeRepository
{
    /// <summary>One page of people matching <paramref name="search"/>, and how many match in total.</summary>
    Task<(IReadOnlyList<EmployeeRow> Items, int TotalCount)> SearchAsync(
        EmployeeSearch search,
        CancellationToken cancellationToken = default);

    Task<EmployeeRow?> GetAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<UserPayHistory>> GetPayHistoryAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>The pay row in force on <paramref name="day"/>: the latest one dated on or before it.</summary>
    Task<UserPayHistory?> GetPayInForceAsync(long userId, DateOnly day, CancellationToken cancellationToken = default);

    Task<DateOnly?> GetLatestPayDateAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks every active super admin's row until the transaction ends and returns how many
    /// there are, so two people can't each remove "another" super admin at the same moment.
    /// </summary>
    Task<int> LockActiveSuperAdminsAsync(CancellationToken cancellationToken = default);

    /// <summary>The person's latest period of employment, if they ever started. Tracked.</summary>
    Task<EmploymentPeriod?> LatestPeriodAsync(long userId, CancellationToken cancellationToken = default);

    void AddPeriod(EmploymentPeriod period);
}

/// <param name="Domains">Home domains the caller can see.</param>
/// <param name="IncludeSuperAdmins">Super admins have no domain; listed only for callers who see both domains.</param>
public sealed record EmployeeSearch(
    IReadOnlyList<string> Domains,
    bool IncludeSuperAdmins,
    string? Text,
    long? TeamId,
    string? Role,
    string? Domain,
    UserStatus? Status,
    int Page,
    int PageSize);

public sealed record EmployeeRow(
    long Id,
    string Email,
    string? FirstName,
    string? LastName,
    string? Phone,
    string Role,
    string? Domain,
    long? TeamId,
    string? TeamName,
    string? Position,
    UserStatus Status,
    bool HasPermissionOverrides,
    DateOnly? JoinedOn,
    DateOnly? LeftOn,
    DateTime? LastSeenAt,
    DateTime CreatedAt);
