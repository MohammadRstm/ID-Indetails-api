namespace ID_Indetails.api.Enums;

/// <summary>How the person talked to Proton: typed in the chat, or spoke through a voice session.</summary>
public enum ProtonChannel
{
    Text,
    Voice,
    /// <summary>A reply suggested in a discussion with a client: one model call, no tools, no conversation.</summary>
    Suggestion,
}
