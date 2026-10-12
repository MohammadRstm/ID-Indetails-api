using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>A proposed change waiting for the user: what the confirmation card shows.</summary>
/// <param name="Target">The record it changes, when it changes one: a later card for the same tool and record replaces this one.</param>
public sealed record ProtonPendingAction(Guid Id, string Tool, ProtonActionRisk Risk, ProtonPreview Preview, DateTime ExpiresAt, Tools.ProtonRef? Target = null);
