namespace ID_Indetails.api.Business.Push;

/// <summary>
/// A person's quiet hours, in minutes after midnight in the company's time zone. A push inside them is still delivered,
/// silently. The range may cross midnight (22:00 to 07:00); the end minute itself is no longer quiet.
/// </summary>
public static class QuietHours
{
    /// <summary>22:00, for whoever never chose their own.</summary>
    public const int DefaultFromMinute = 22 * 60;

    /// <summary>07:00.</summary>
    public const int DefaultToMinute = 7 * 60;

    public const int LastMinute = 24 * 60 - 1;

    /// <summary>Both null means no quiet hours; equal start and end mean none either.</summary>
    public static bool Contains(int? fromMinute, int? toMinute, TimeOnly companyTime)
    {
        if (fromMinute is not { } from || toMinute is not { } to || from == to)
        {
            return false;
        }

        var minute = companyTime.Hour * 60 + companyTime.Minute;
        return from < to
            ? minute >= from && minute < to
            : minute >= from || minute < to;
    }
}
