using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Authorization.Handlers;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Hubs;
using ID_Indetails.api.Middlewares;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
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
        services.AddOptions<MobileSessionOptions>().BindConfiguration(MobileSessionOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SetupOptions>().BindConfiguration(SetupOptions.SectionName);
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddOptions<PortalOptions>().BindConfiguration(PortalOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var portal = configuration.GetSection(PortalOptions.SectionName).Get<PortalOptions>() ?? new PortalOptions();

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

                // WebSockets can't send headers, so the live connection alone may pass the token in the query string.
                // Request logging stays at Warning, so query strings aren't logged.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments(LiveHub.Path)
                            && context.Request.Query["access_token"] is { Count: > 0 } token)
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },

                    // Sessions a super admin ended stay ended (SessionRules), read from the per-user access cache.
                    OnTokenValidated = async context =>
                    {
                        if (!long.TryParse(context.Principal?.FindFirst(AuthClaimTypes.UserId)?.Value, out var userId)
                            || context.SecurityToken is not JsonWebToken token)
                        {
                            context.Fail("The token has no user.");
                            return;
                        }

                        var access = await context.HttpContext.RequestServices.GetRequiredService<IPermissionService>()
                            .GetAccessAsync(userId, context.HttpContext.RequestAborted);
                        if (!SessionRules.Accepts(token.IssuedAt, access.SessionsRevokedAt, access.SessionsValidFrom))
                        {
                            context.Fail("The session was ended.");
                        }
                    },
                };
            })
            // The client portal: the same issuer and key, its own audience and its own id claim, so a client's token is
            // refused by every staff endpoint and a staff token by every portal endpoint (docs/client-portal.md).
            .AddJwtBearer(AuthorizationPolicies.PortalScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = portal.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = AuthClaimTypes.PortalAccountId,
                    AuthenticationType = AuthorizationPolicies.PortalScheme,
                };
                options.Events = new JwtBearerEvents
                {
                    // An account that is no longer active, or whose sessions were ended, is refused at once.
                    OnTokenValidated = async context =>
                    {
                        if (!long.TryParse(context.Principal?.FindFirst(AuthClaimTypes.PortalAccountId)?.Value, out var accountId)
                            || context.Principal?.FindFirst(AuthClaimTypes.UserId) is not null
                            || context.SecurityToken is not JsonWebToken token)
                        {
                            context.Fail("The token has no portal account.");
                            return;
                        }

                        var account = await context.HttpContext.RequestServices.GetRequiredService<IPortalAccountRepository>()
                            .GetSessionStateAsync(accountId, context.HttpContext.RequestAborted);
                        if (account is not { Status: PortalAccountStatus.Active }
                            || !SessionRules.Accepts(token.IssuedAt, null, account.SessionsValidFrom))
                        {
                            context.Fail("The session was ended.");
                        }
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = AuthorizationPolicies.Default;
            // An endpoint with no authorization attribute still requires a signed-in user, so a forgotten
            // attribute fails closed. Public endpoints opt out with [AllowAnonymous].
            options.FallbackPolicy = AuthorizationPolicies.Default;
            options.AddPolicy(AuthorizationPolicies.AllowPendingPasswordChange, p => p.RequireAuthenticatedUser());
            options.AddPolicy(AuthorizationPolicies.Portal, p => p
                .AddAuthenticationSchemes(AuthorizationPolicies.PortalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AuthClaimTypes.PortalAccountId));
        });
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, PasswordChangeCompletedHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<RefreshTokenCookie>();
        services.AddSingleton<PortalRefreshCookie>();
        services.AddScoped<ICurrentPortalAccount, CurrentPortalAccount>();

        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(options =>
        {
            // Applies to every endpoint, so new endpoints are covered without an attribute.
            // Runs after authentication (see Program.cs), so signed-in users get their own bucket.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var userId = context.User.FindFirst(AuthClaimTypes.UserId)?.Value;
                return userId is not null
                    ? RateLimitPartition.GetFixedWindowLimiter("user:" + userId, _ => PerMinute(limits.UserPermitsPerMinute))
                    : RateLimitPartition.GetFixedWindowLimiter(
                        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                        _ => PerMinute(limits.AnonymousPermitsPerMinute));
            });

            options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));

            options.AddPolicy(RateLimitPolicies.Refresh, context => RateLimitPartition.GetFixedWindowLimiter(
                "refresh:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => PerMinute(limits.RefreshPermitsPerMinute)));

            options.AddPolicy(RateLimitPolicies.Credential, context => RateLimitPartition.GetFixedWindowLimiter(
                "credential:" + (context.User.FindFirst(AuthClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => PerMinute(limits.AuthPermitsPerMinute)));

            options.AddPolicy(RateLimitPolicies.Email, context => RateLimitPartition.GetFixedWindowLimiter(
                "email:" + (context.User.FindFirst(AuthClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => PerMinute(limits.EmailPermitsPerMinute)));

            options.AddPolicy(RateLimitPolicies.Export, context => RateLimitPartition.GetFixedWindowLimiter(
                "export:" + (context.User.FindFirst(AuthClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => PerMinute(limits.ExportPermitsPerMinute)));

            options.AddPolicy(RateLimitPolicies.Upload, context => RateLimitPartition.GetFixedWindowLimiter(
                "upload:" + (context.User.FindFirst(AuthClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => PerMinute(limits.UploadPermitsPerMinute)));

            options.AddPolicy(RateLimitPolicies.BugReport, context => RateLimitPartition.GetFixedWindowLimiter(
                "bug-report:" + (context.User.FindFirst(AuthClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.BugReportPermitsPerHour, Window = TimeSpan.FromHours(1) }));

            options.AddPolicy(RateLimitPolicies.Proton, context => RateLimitPartition.GetFixedWindowLimiter(
                "proton:" + (context.User.FindFirst(AuthClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => PerMinute(limits.ProtonPermitsPerMinute)));

            options.AddPolicy(RateLimitPolicies.Setup, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.SetupPermitsPerHour, Window = TimeSpan.FromHours(1) }));

            options.OnRejected = (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(SecurityLog.Category).LogWarning(
                    "Rate limit reached on {Method} {Path} by {Caller}.",
                    http.Request.Method, SecurityLog.OneLine(http.Request.Path.Value), SecurityLog.Caller(http));
                return new ValueTask(ApiErrorWriter.WriteAsync(
                    http,
                    ErrorType.RateLimited,
                    ErrorCodes.TooManyRequests,
                    "Too many requests. Please wait a minute and try again.",
                    cancellationToken: cancellationToken));
            };
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions PerMinute(int permits)
        => new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) };
}
