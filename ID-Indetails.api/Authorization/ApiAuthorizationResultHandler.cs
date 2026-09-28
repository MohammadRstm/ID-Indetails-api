using ID_Indetails.api.Authorization.Requirements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Middlewares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace ID_Indetails.api.Authorization;

/// <summary>
/// Turns authorization failures into the uniform error response: 401 when not signed in,
/// 403 <c>password_change_required</c> while a generated password is pending, 403 otherwise.
/// </summary>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await ApiErrorWriter.WriteAsync(
                context, ErrorType.Authentication, ErrorCodes.Unauthenticated, "You need to sign in.");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            var passwordPending = authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<PasswordChangeCompletedRequirement>()
                .Any() == true;

            await ApiErrorWriter.WriteAsync(
                context,
                ErrorType.Authorization,
                passwordPending ? ErrorCodes.PasswordChangeRequired : ErrorCodes.Forbidden,
                passwordPending
                    ? "You must change your temporary password before continuing."
                    : "You don't have permission to do this.");
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
