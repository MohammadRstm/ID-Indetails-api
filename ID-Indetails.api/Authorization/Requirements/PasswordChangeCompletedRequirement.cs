using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Authorization.Requirements;

/// <summary>
/// Blocks accounts that still use a generated password. Part of every policy except the
/// few endpoints needed to replace it (change password, current user, logout).
/// </summary>
public sealed class PasswordChangeCompletedRequirement : IAuthorizationRequirement;
