using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>A proposed change waiting for the user: what the confirmation card shows.</summary>
public sealed record ProtonPendingAction(Guid Id, string Tool, ProtonActionRisk Risk, ProtonPreview Preview, DateTime ExpiresAt);
