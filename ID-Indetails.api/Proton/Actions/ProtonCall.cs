using System.Text.Json;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>
/// What a proposal stores to be carried out (the arguments column): the user's access when it was proposed, and the
/// tool's own arguments. A confirmation under different access is refused: the card was made for other permissions.
/// </summary>
public sealed record ProtonCall(string Fingerprint, JsonElement Args);
