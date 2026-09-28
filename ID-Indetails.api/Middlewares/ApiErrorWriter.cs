using System.Diagnostics;
using ID_Indetails.api.DTO.Response.Error;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Writes the uniform <see cref="ApiErrorResponse"/>. Every error path (exceptions,
/// authentication, authorization, rate limiting, model binding) goes through here, so the
/// client never has to guess the error format.
/// </summary>
public static class ApiErrorWriter
{
    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Authentication => StatusCodes.Status401Unauthorized,
        ErrorType.Authorization => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ApiErrorResponse Build(
        HttpContext context,
        ErrorType type,
        string code,
        string message,
        IDictionary<string, string[]>? errors = null) => new()
    {
        Type = type,
        Code = code,
        Message = message,
        Errors = errors,
        TraceId = TraceIdOf(context),
    };

    public static Task WriteAsync(
        HttpContext context,
        ErrorType type,
        string code,
        string message,
        IDictionary<string, string[]>? errors = null,
        CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = StatusCodeFor(type);
        return context.Response.WriteAsJsonAsync(
            Build(context, type, code, message, errors),
            cancellationToken);
    }

    public static string TraceIdOf(HttpContext context)
        => Activity.Current?.Id ?? context.TraceIdentifier;
}
