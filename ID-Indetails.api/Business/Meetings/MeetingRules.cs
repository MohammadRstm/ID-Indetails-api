using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Meetings;

/// <summary>
/// <list type="bullet">
/// <item>Project meetings belong to projects of either domain. New ones are set on active projects only; those already
/// set can be moved or removed while the project is active or on hold. Everyone who sees the project sees them.</item>
/// <item>General meetings have no project: whoever manages meetings sets them, for the people they choose (at least
/// one). They belong to a domain, or to the whole company, and are seen by their attendees, whoever set them, and
/// whoever manages meetings where they belong (see <see cref="CanSeeGeneral"/>).</item>
/// <item>People with <c>meeting:create</c> set project meetings and move or remove their own; <c>meeting:manage</c> covers every meeting.</item>
/// <item>A meeting starts and ends on the same company-time-zone day, and lasts at least 5 minutes. Past days are fine.</item>
/// <item>Meetings are not work time: they never count in hours, and don't block logging.</item>
/// </list>
/// </summary>
public static class MeetingRules
{
    public const int MinMinutes = 5;
    public const int MaxTopicLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxAttendees = 100;

    /// <param name="Domain">Null for a company-wide meeting.</param>
    public sealed record GeneralMeeting(string? Domain, long? CreatedById, IReadOnlyCollection<long> AttendeeIds);

    /// <param name="SeesCompany">Sees the whole company (<c>company:view</c>), as super admins do.</param>
    public sealed record Viewer(long UserId, bool ManagesMeetings, bool SeesCompany, IReadOnlyCollection<string> Domains);

    public static Viewer ViewerOf(UserAccess access)
        => new(access.UserId, access.Has(PermissionCodes.MeetingManage), access.Has(PermissionCodes.CompanyView), access.VisibleDomains);

    /// <summary>
    /// Anyone who manages meetings changes any of them; people who may only set meetings change the ones they set.
    /// </summary>
    public static RuleDenial? CheckChange(bool managesMeetings, bool setsMeetings, long? createdById, long actorId)
        => managesMeetings || (setsMeetings && createdById == actorId)
            ? null
            : RuleDenial.Forbidden(ErrorCodes.Forbidden, "Only whoever set this meeting, or someone who manages meetings, can change it.");

    /// <summary>A meeting already set can change while its project is open (active or on hold).</summary>
    public static RuleDenial? CheckProject(ProjectStatus status)
        => status is ProjectStatus.Active or ProjectStatus.Hold
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.ProjectClosed, "This project is finished. Reopen it to set meetings.");

    /// <summary>A new meeting is set on an active project only.</summary>
    public static RuleDenial? CheckNewMeeting(ProjectStatus status)
        => status == ProjectStatus.Hold
            ? RuleDenial.BusinessRule(ErrorCodes.ProjectNotActive, "This project is on hold. Meetings are set on active projects only.")
            : CheckProject(status);

    public static bool CanSeeGeneral(GeneralMeeting meeting, Viewer viewer)
        => meeting.CreatedById == viewer.UserId
            || meeting.AttendeeIds.Contains(viewer.UserId)
            || (viewer.ManagesMeetings && (meeting.Domain is null ? viewer.SeesCompany : viewer.Domains.Contains(meeting.Domain)));

    /// <param name="attendeeIds">Everyone chosen but whoever sets it.</param>
    /// <param name="invitable">The active people its setter may choose.</param>
    public static RuleDenial? CheckAttendees(IReadOnlyCollection<long> attendeeIds, IReadOnlySet<long> invitable)
    {
        if (attendeeIds.Count == 0)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "attendeeIds", "Choose who the meeting is with.");
        }

        return attendeeIds.All(invitable.Contains)
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "attendeeIds", "Choose the attendees from the people of this meeting's domain.");
    }

    /// <param name="startDay">The company-time-zone day the meeting starts on.</param>
    /// <param name="endDay">The company-time-zone day of the last minute of the meeting.</param>
    public static RuleDenial? CheckTimes(DateTime startsAt, DateTime endsAt, DateOnly startDay, DateOnly endDay)
    {
        if ((endsAt - startsAt).TotalMinutes < MinMinutes)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "endsAt", $"A meeting lasts at least {MinMinutes} minutes.");
        }

        return startDay == endDay
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "endsAt", "A meeting starts and ends on the same day.");
    }
}
