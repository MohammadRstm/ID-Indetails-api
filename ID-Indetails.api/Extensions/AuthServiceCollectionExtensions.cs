using System.Text;
using System.Threading.RateLimiting;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Authorization.Handlers;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Middlewares;
using ID_Indetails.api.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace ID_Indetails.api.Extensions;

public static class AuthServiceCollectionExtensions
{
    /// <summary>JWT authentication, permission-based authorization, the refresh cookie and rate limits.</summary>
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RefreshTokenOptions>().BindConfiguration(RefreshTokenOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => Enum.TryParse<SameSiteMode>(o.SameSite, true, out _), "RefreshToken:SameSite must be Strict, Lax or None.")
            .ValidateOnStart();
        services.AddOptions<SetupOptions>().BindConfiguration(SetupOptions.SectionName);
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep claim names exactly as issued ("sub", "role", ...).
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = AuthClaimTypes.UserId,
                    RoleClaimType = AuthClaimTypes.Role,
                };
            });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = AuthorizationPolicies.Default;
            options.AddPolicy(AuthorizationPolicies.AllowPendingPasswordChange, p => p.RequireAuthenticatedUser());
        });
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, PasswordChangeCompletedHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<RefreshTokenCookie>();

        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));

            options.AddPolicy(RateLimitPolicies.Setup, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.SetupPermitsPerHour, Window = TimeSpan.FromHours(1) }));

            options.OnRejected = (context, cancellationToken) => new ValueTask(ApiErrorWriter.WriteAsync(
                context.HttpContext,
                ErrorType.RateLimited,
                ErrorCodes.TooManyRequests,
                "Too many attempts. Please wait a minute and try again.",
                cancellationToken: cancellationToken));
        });

        return services;
    }
}
