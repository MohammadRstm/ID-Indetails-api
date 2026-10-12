using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Push;

/// <summary>
/// When the push-only reminders go out (decision 7 of the phone app's plan). They are checked hourly; each goes to a
/// person at most once a day.
/// </summary>
public static class ReminderRules
{
    /// <summary>The log-your-hours reminder comes in the last hour of the working day.</summary>
    public const int LogWindowMinutes = 60;

    /// <summary>The labour-week reminder: Saturday, the last day of a site week, from 15:00.</summary>
    public const int LabourFromMinute = 15 * 60;

    /// <summary>How far back recording anything on a site makes someone its reporter.</summary>
    public const int LabourLookbackDays = 28;

    /// <summary>
    /// In the hour before the person's working day ends, when what they logged within their hours is less than the day
    /// requires. Never on a day off or an excused day.
    /// </summary>
    /// <param name="hours">The person's hours today, null on a day off.</param>
    /// <param name="loggedInsideMinutes">Today's logged minutes that aren't overtime.</param>
    public static bool LogReminderDue(WorkHours? hours, int minuteOfDay, int loggedInsideMinutes, bool excusedToday)
        => hours is not null
            && !excusedToday
            && minuteOfDay >= hours.End - LogWindowMinutes
            && minuteOfDay < hours.End
            && loggedInsideMinutes < hours.Required;

    public static bool LabourReminderDue(DateOnly today, int minuteOfDay)
        => today.DayOfWeek == DayOfWeek.Saturday && minuteOfDay >= LabourFromMinute;
}
