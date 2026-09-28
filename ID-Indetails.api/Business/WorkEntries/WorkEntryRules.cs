using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.WorkEntries;

/// <summary>
/// <list type="bullet">
/// <item>Work is logged on Design projects that are active, or on hold when the domain allows hold logging.</item>
/// <item>An entry is 5 minutes to 16 hours long, in whole minutes, and ends no later than now unless future entries are allowed.</item>
/// <item>A person's entries may not overlap unless the domain allows it. Back-to-back entries don't overlap.</item>
/// <item>An entry can be edited, deleted or restored within the edit window, counted from when it was logged,
/// and only while its project still accepts work.</item>
/// </list>
/// </summary>
public static class WorkEntryRules
{
    public const int MinMinutes = 5;
    public const int MaxMinutes = 16 * 60;
    public const int MaxDescriptionLength = 2000;

    /// <summary>An entry, or its new times, when checking for overlaps.</summary>
    public sealed record Span(long Id, DateTime Start, DateTime End, string ProjectTitle);

    public static bool AcceptsWork(ProjectStatus status, bool holdLogging)
        => status == ProjectStatus.Active || (status == ProjectStatus.Hold && holdLogging);

    public static RuleDenial? CheckProject(string domainCode, ProjectStatus status, bool holdLogging)
    {
        if (domainCode != DomainCodes.Design)
        {
            return RuleDenial.BusinessRule(ErrorCodes.NotForThisDomain, "Work is only logged on Design projects.");
        }

        return AcceptsWork(status, holdLogging)
            ? null
            : RuleDenial.BusinessRule(
                ErrorCodes.ProjectNotAcceptingWork,
                status == ProjectStatus.Hold
                    ? "This project is on hold. New work entries are paused until it resumes."
                    : "This project is closed. Only active projects accept work.");
    }

    public static RuleDenial? CheckEntry(string description, DateTime end, DateTime now, bool requireDescription, bool allowFuture)
    {
        if (requireDescription && string.IsNullOrWhiteSpace(description))
        {
            return RuleDenial.Invalid(ErrorCodes.DescriptionRequired, "description", "Say what you worked on.");
        }

        return !allowFuture && end > now
            ? RuleDenial.BusinessRule(ErrorCodes.EntryInFuture, "That ends in the future. Work can only be logged up to now.")
            : null;
    }

    /// <summary>
    /// The first pair of spans that overlap, checking only <paramref name="changed"/> against each other
    /// and against <paramref name="others"/> (the person's other entries).
    /// </summary>
    public static RuleDenial? CheckOverlap(IReadOnlyList<Span> changed, IReadOnlyList<Span> others)
    {
        var all = changed.Concat(others.Where(o => changed.All(c => c.Id != o.Id))).ToList();
        foreach (var span in changed)
        {
            var clash = all.FirstOrDefault(o => !ReferenceEquals(o, span) && o.Start < span.End && o.End > span.Start);
            if (clash is not null)
            {
                return RuleDenial.BusinessRule(
                    ErrorCodes.EntryOverlaps,
                    $"This overlaps with another of your entries ({clash.ProjectTitle}).");
            }
        }

        return null;
    }

    public static bool IsWithinEditWindow(DateTime createdAt, DateTime now, EditWindow window) => window switch
    {
        EditWindow.None => false,
        EditWindow.OneDay => now - createdAt < TimeSpan.FromDays(1),
        EditWindow.SevenDays => now - createdAt < TimeSpan.FromDays(7),
        _ => true,
    };

    public static RuleDenial? CheckChangeable(DateTime createdAt, DateTime now, EditWindow window, ProjectStatus projectStatus, bool holdLogging)
    {
        if (!IsWithinEditWindow(createdAt, now, window))
        {
            return RuleDenial.BusinessRule(ErrorCodes.EditWindowClosed, "That entry can no longer be changed: it is past the edit window.");
        }

        return AcceptsWork(projectStatus, holdLogging)
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.ProjectNotAcceptingWork, "Entries of a project that no longer accepts work can't be changed.");
    }

    /// <summary>Times from JSON as UTC: offsets are converted, times without one are taken as UTC.</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };
}
