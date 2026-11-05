namespace ID_Indetails.api.DTO.Response.Push;

/// <summary>The server's VAPID public key, which a browser subscribes with. Null while browsers get no pushes.</summary>
public sealed record WebPushKeyResponse(string? PublicKey);
