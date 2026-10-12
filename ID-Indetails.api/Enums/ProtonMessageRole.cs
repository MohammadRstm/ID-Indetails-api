namespace ID_Indetails.api.Enums;

/// <summary>Who wrote a saved chat message.</summary>
public enum ProtonMessageRole
{
    User,
    Proton,
}

/// <summary>How the user asked a saved question.</summary>
public enum ProtonMessageOrigin
{
    Typed,
    Choice,
    Suggestion,
    Spoken,
}
