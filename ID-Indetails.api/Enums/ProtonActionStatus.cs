namespace ID_Indetails.api.Enums;

/// <summary>Where a change Proton proposed stands. Only <see cref="Pending"/> can be confirmed, and only once.</summary>
public enum ProtonActionStatus
{
    /// <summary>Proposed, waiting for the user to confirm or cancel.</summary>
    Pending,

    /// <summary>Confirmed and running. Never run again, even if the API stopped half way.</summary>
    Executing,

    Succeeded,

    /// <summary>The existing service refused or failed; nothing it guards was changed.</summary>
    Failed,

    Cancelled,

    /// <summary>A newer proposal in the same conversation replaced it.</summary>
    Superseded,

    Expired,
}

/// <summary>How much a Proton action can change. High-risk actions (money, deletion, irreversible) aren't offered.</summary>
public enum ProtonActionRisk
{
    Low,
    Medium,
}
