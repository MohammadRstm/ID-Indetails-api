using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Meetings;

/// <summary>
/// <list type="bullet">
/// <item>Meetings belong to Design projects that are active or on hold.</item>
/// <item>People with <c>meeting:create</c> set meetings and move or remove their own; <c>meeting:manage</c> covers every meeting.</item>
/// <item>A meeting starts and ends on the same company-time-zone day, and lasts at least 5 minutes. Past days are fine.</item>
/// <item>Meetings are not work time: they never count in hours, and don't block logging.</item>
/// </list>
/// </summary>
public static class MeetingRules
{
    public const int MinMinutes = 5;
    public const int MaxTopicLength = 200;
    public const int MaxDescriptionLength = 2000;

    /// <summary>
    /// Anyone who manages meetings changes any of them; people who may only set meetings change the ones they set.
    /// </summary>
    public static RuleDenial? CheckChange(bool managesMeetings, bool setsMeetings, long? createdById, long actorId)
        => managesMeetings || (setsMeetings && createdById == actorId)
            ? null
            : RuleDenial.Forbidden(ErrorCodes.Forbidden, "Only whoever set this meeting, or someone who manages meetings, can change it.");

    public static RuleDenial? CheckProject(string domainCode, ProjectStatus status)
    {
        if (domainCode != DomainCodes.Design)
        {
            return RuleDenial.BusinessRule(ErrorCodes.NotForThisDomain, "Meetings are only set on Design projects.");
        }

        return status is ProjectStatus.Active or ProjectStatus.Hold
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.ProjectClosed, "This project is finished. Reopen it to set meetings.");
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
