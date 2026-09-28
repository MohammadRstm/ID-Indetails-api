using ID_Indetails.api.Authorization.Requirements;
using ID_Indetails.api.Constants;
using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Authorization.Handlers;

public sealed class PasswordChangeCompletedHandler : AuthorizationHandler<PasswordChangeCompletedRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PasswordChangeCompletedRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(AuthClaimTypes.MustChangePassword)?.Value != "true")
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
