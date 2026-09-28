namespace ID_Indetails.api.Constants;

/// <summary>Machine-readable error codes returned in <c>ApiErrorResponse.Code</c>.</summary>
public static class ErrorCodes
{
    // Generic
    public const string ValidationFailed = "validation_failed";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string TooManyRequests = "too_many_requests";
    public const string InternalError = "internal_error";

    // Authentication
    public const string InvalidCredentials = "invalid_credentials";
    public const string RefreshTokenInvalid = "refresh_token_invalid";
    public const string RefreshTokenReused = "refresh_token_reused";
    public const string PasswordChangeRequired = "password_change_required";
    public const string CurrentPasswordIncorrect = "current_password_incorrect";

    // Accounts and invitations
    public const string EmailAlreadyRegistered = "email_already_registered";
    public const string InvitationInvalid = "invitation_invalid";
    public const string InvitationNotAllowed = "invitation_not_allowed";
    public const string TeamNotInDomain = "team_not_in_domain";
    public const string UserNotInvited = "user_not_invited";
    public const string UserDeactivated = "user_deactivated";

    // Permissions
    public const string PermissionsLocked = "permissions_locked";
    public const string PermissionChangeNotAllowed = "permission_change_not_allowed";
    public const string UnknownPermission = "unknown_permission";

    // Dependencies
    public const string EmailDeliveryFailed = "email_delivery_failed";
}
