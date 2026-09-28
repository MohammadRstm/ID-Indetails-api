using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Labour.Abstraction;

/// <summary>A reported week, with its days and the name of who submitted it.</summary>
public sealed record LabourWeekRecord(LabourWeek Week, string SubmittedByName);

/// <summary>A dated labour default and the name of who set it (none for defaults set by the system).</summary>
public sealed record LabourDefaultRecord(ProjectLabourDefault Default, string? SetByName);

public interface ILabourRepository
{
    /// <summary>The project's reported weeks with their days, not tracked.</summary>
    Task<IReadOnlyList<LabourWeekRecord>> ListWeeksAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>One reported week with its days, not tracked.</summary>
    Task<LabourWeekRecord?> GetWeekAsync(long projectId, DateOnly weekStart, CancellationToken cancellationToken = default);

    /// <summary>One reported week with its days, tracked.</summary>
    Task<LabourWeek?> GetWeekForUpdateAsync(long projectId, DateOnly weekStart, CancellationToken cancellationToken = default);

    void AddWeek(LabourWeek week);

    void RemoveDay(LabourWeekDay day);

    /// <summary>Every dated default of the project, not tracked.</summary>
    Task<IReadOnlyList<LabourDefaultRecord>> ListDefaultsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The default dated <paramref name="effectiveFrom"/>, tracked.</summary>
    Task<ProjectLabourDefault?> GetDefaultForUpdateAsync(long projectId, DateOnly effectiveFrom, CancellationToken cancellationToken = default);

    void AddDefault(ProjectLabourDefault labourDefault);
}
