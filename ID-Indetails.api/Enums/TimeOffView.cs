namespace ID_Indetails.api.Enums;

/// <summary>Which requests for time off a super admin lists: waiting, approved with days still to come, or the rest.</summary>
public enum TimeOffView
{
    Pending,
    Current,
    Past,
}
