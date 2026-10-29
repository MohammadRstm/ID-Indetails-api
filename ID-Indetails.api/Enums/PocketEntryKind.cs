namespace ID_Indetails.api.Enums;

/// <summary>Why money went into or out of the Execution pocket money.</summary>
public enum PocketEntryKind
{
    /// <summary>A super admin moved part of a site's money into the pocket.</summary>
    Contribution,

    /// <summary>An approved request paid money out to a site.</summary>
    Payout,
}
