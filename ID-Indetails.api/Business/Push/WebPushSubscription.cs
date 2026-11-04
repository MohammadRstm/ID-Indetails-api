namespace ID_Indetails.api.Business.Push;

/// <summary>A browser's push subscription: where its pushes are posted, and the two keys (base64url) they're encrypted to.</summary>
public sealed record WebPushSubscription(string Endpoint, string P256dh, string Auth);
