namespace ID_Indetails.api.Constants;

/// <summary>What a push is about when it isn't a notification row: <c>PushData.kind</c> is a notification kind or one of these.</summary>
public static class PushKinds
{
    /// <summary>A team or direct message.</summary>
    public const string Message = "message";

    /// <summary>Push only: a Design person's working day ends soon and some of its hours aren't logged.</summary>
    public const string LogReminder = "log_reminder";

    /// <summary>Push only: on Saturday afternoon, a site's current labour week isn't reported yet.</summary>
    public const string LabourWeekReminder = "labour_week_reminder";
}
