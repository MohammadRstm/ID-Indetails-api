namespace ID_Indetails.api.DTO.Request.Push;

/// <summary>This browser's push subscription, as <c>PushSubscription.toJSON()</c> gives it (its two keys flattened).</summary>
public sealed record RegisterWebPushRequest
{
    public string Endpoint { get; init; } = string.Empty;

    public string P256dh { get; init; } = string.Empty;

    public string Auth { get; init; } = string.Empty;
}
