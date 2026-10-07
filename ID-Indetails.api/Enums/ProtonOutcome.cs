namespace ID_Indetails.api.Enums;

/// <summary>How a Proton turn ended.</summary>
public enum ProtonOutcome
{
    Answered,

    /// <summary>The model declined to answer (a provider safety refusal).</summary>
    Refused,

    /// <summary>Stopped at a turn limit (round trips, tool calls or output length) and answered with what it had.</summary>
    Limited,

    /// <summary>The provider or Proton itself failed; the user got an error.</summary>
    Failed,
}
