namespace ID_Indetails.api.Enums;

/// <summary>What an hours-ledger excuse covers.</summary>
public enum ExcuseKind
{
    /// <summary>A number of minutes in the month.</summary>
    Minutes,

    /// <summary>Specific days off (see hour_excuse_days); each is worth that day's required minutes.</summary>
    Days,

    /// <summary>The whole month.</summary>
    WholeMonth,
}
