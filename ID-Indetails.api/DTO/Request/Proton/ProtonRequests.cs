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
}

/// <summary>The page the user is on, by meaning. <see cref="EntityId"/> comes with <see cref="EntityType"/>.</summary>
public sealed record ProtonPageContext
{
    /// <summary>design, execution or company.</summary>
    public string Area { get; init; } = string.Empty;

    public ProtonPage? Page { get; init; }

    public ProtonRefType? EntityType { get; init; }

    public long? EntityId { get; init; }

    /// <summary>The project tab, on a project page.</summary>
    public ProtonProjectSection? Section { get; init; }

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
