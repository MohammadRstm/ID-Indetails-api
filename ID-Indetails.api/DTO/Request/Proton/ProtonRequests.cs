using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;

namespace ID_Indetails.api.DTO.Request.Proton;

public sealed record AskProtonRequest
{
    /// <summary>Leave out to start a new conversation; the reply says which one it is.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>The question. Not needed when a button of the last reply answers it (<see cref="Choice"/>, <see cref="Reply"/> or <see cref="Suggestion"/>).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Where the user is in the app, so "it" and "this project" make sense. Never trusted as data or access.</summary>
    public ProtonPageContext? Page { get; init; }

    /// <summary>The option the user picked from the buttons of the last reply; checked against what was offered.</summary>
    public ProtonChoiceRequest? Choice { get; init; }

    /// <summary>The quick reply the user clicked; checked against what was offered.</summary>
    public ProtonQuickReply? Reply { get; init; }

    /// <summary>The place in the list of the suggested reply the user tapped; the server takes its text from what it offered.</summary>
    public int? Suggestion { get; init; }

    /// <summary>
    /// A question the user said in a voice session: <see cref="Text"/> is what the voice model heard. It is answered
    /// like a typed one, except that speech can't start the gated kinds of change, and it is asked only once.
    /// </summary>
    public ProtonVoiceTurnRequest? Voice { get; init; }
}

/// <summary>Which voice session a spoken question came from, and the provider's id for that request.</summary>
public sealed record ProtonVoiceTurnRequest
{
    public string SessionId { get; init; } = string.Empty;

    public string DelegationId { get; init; } = string.Empty;
}

/// <summary>Starting a voice session: the browser's WebRTC offer, and the conversation to carry on (or none for a new one).</summary>
public sealed record StartProtonVoiceRequest
{
    public Guid? ConversationId { get; init; }

    public string Sdp { get; init; } = string.Empty;
}

/// <summary>The page the user is on, by meaning. <see cref="EntityId"/> comes with <see cref="EntityType"/>.</summary>
public sealed record ProtonPageContext
{
    /// <summary>design, execution, website or company.</summary>
    public string Area { get; init; } = string.Empty;

    public ProtonPage? Page { get; init; }

    public ProtonRefType? EntityType { get; init; }

    public long? EntityId { get; init; }

    /// <summary>The project tab, on a project page.</summary>
    public ProtonProjectSection? Section { get; init; }

    /// <summary>The part of the files or client portal tab: a section, the client's access or the discussion.</summary>
    public ProtonProjectPart? Part { get; init; }

    /// <summary>The form, dialog or drawer open on the page, if it is one Proton knows.</summary>
    public ProtonSurface? Surface { get; init; }
}

public sealed record ProtonChoiceRequest
{
    public ProtonRefType Type { get; init; }

    public long Id { get; init; }
}

public sealed record TopUpProtonBudgetRequest
{
    /// <summary>Added to this month's budget, in USD.</summary>
    public decimal AmountUsd { get; init; }

    public string? Note { get; init; }
}

/// <summary>A reply suggestion for the discussion with a Design client project's client.</summary>
public sealed record SuggestReplyRequest
{
    public long ProjectId { get; init; }
}

/// <summary>Something to remember: words the user typed in that conversation, checked against what the server kept of it.</summary>
public sealed record SaveProtonMemoryRequest
{
    public Guid ConversationId { get; init; }

    public string Text { get; init; } = string.Empty;
}

/// <summary>Confirming or cancelling a proposed action: only its id (in the route) and the conversation it belongs to.</summary>
public sealed record DecideProtonActionRequest
{
    public Guid ConversationId { get; init; }

    /// <summary>For a card that asks for it: the phrase the user typed. Checked against the one stored with the proposal.</summary>
    public string? Typed { get; init; }
}

/// <summary>The person's saved chats, a page at a time, optionally by title.</summary>
public sealed record ProtonConversationListQuery
{
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record RenameProtonConversationRequest
{
    public string Title { get; init; } = string.Empty;
}
