namespace ID_Indetails.api.Enums;

/// <summary>Which salary advances super admins list.</summary>
public enum SalaryAdvanceView
{
    /// <summary>Requests waiting for a decision, oldest first.</summary>
    Pending,
    /// <summary>Given and not paid back yet, people who left with something unsettled included; newest first.</summary>
    Open,
    /// <summary>Paid back, settled, declined, withdrawn or cancelled; newest first.</summary>
    Closed,
}
