using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Middlewares;
using ID_Indetails.api.Services.Common.Implementation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Authorization;

/// <summary>
/// Allows the request only when the <c>X-Setup-Key</c> header matches the configured key
/// (compared in constant time). Runs before model validation, so without the key the
/// endpoint answers 404 for every request and never reveals that it exists.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSetupKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var expected = services.GetRequiredService<IOptions<SetupOptions>>().Value.SuperAdminKey;
        var provided = context.HttpContext.Request.Headers[SetupOptions.SuperAdminKeyHeader].ToString();

        if (!string.IsNullOrEmpty(expected)
            && !string.IsNullOrEmpty(provided)
            && SecureTokens.FixedTimeEquals(provided, expected))
        {
            return;
        }

        services.GetRequiredService<ILogger<RequireSetupKeyAttribute>>()
            .LogWarning("Setup endpoint called with a missing or wrong setup key.");

        context.Result = new NotFoundObjectResult(ApiErrorWriter.Build(
            context.HttpContext, ErrorType.NotFound, ErrorCodes.NotFound, "Not found."));
    }
}
