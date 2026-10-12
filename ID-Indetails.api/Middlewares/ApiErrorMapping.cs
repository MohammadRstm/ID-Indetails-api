using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ID_Indetails.api.Middlewares;

/// <summary>An exception as the client may see it: the parts of the uniform error contract.</summary>
public sealed record ApiErrorShape(ErrorType Type, string Code, string Message, IDictionary<string, string[]>? Errors = null);

/// <summary>
/// Which error an exception becomes, and how it is logged: shared by <see cref="GlobalExceptionHandler"/> and by any
/// response that has already started (a stream), so both say the same thing in the same shape.
/// <list type="bullet">
/// <item><see cref="ValidationException"/> (FluentValidation): validation, with field errors.</item>
/// <item>A number too large for its database column: validation, said in words.</item>
/// <item><see cref="AppException"/>: its own type, code and client-safe message.</item>
/// <item>Anything else: logged in full, and the client gets a generic error with only a trace id.</item>
/// </list>
/// </summary>
public static class ApiErrorMapping
{
    public static ApiErrorShape Of(Exception exception, ILogger logger, HttpContext httpContext)
    {
        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                return new ApiErrorShape(ErrorType.Validation, ErrorCodes.ValidationFailed, "One or more fields are invalid.", errors);

            // A number the validators let through that still doesn't fit its column (a sum, a product, a
            // conversion): the client's request, not a server fault, said in words.
            case DbUpdateException { InnerException: MySqlException { ErrorCode: MySqlErrorCode.WarningDataOutOfRange } }:
                logger.LogWarning(exception, "A value too large for its column. TraceId {TraceId}", ApiErrorWriter.TraceIdOf(httpContext));
                return new ApiErrorShape(ErrorType.Validation, ErrorCodes.ValidationFailed, "A number here is too large to be saved.");

            case AppException app:
                // Expected outcome of a request, not a server fault: no stack trace needed.
                logger.LogInformation(
                    "Request failed with {ErrorType}/{ErrorCode}: {ErrorMessage}",
                    app.Type, app.Code, app.Message);
                return new ApiErrorShape(app.Type, app.Code, app.Message, app.Errors);

            default:
                logger.LogError(
                    exception,
                    "Unhandled exception for {Method} {Path}. TraceId {TraceId}",
                    httpContext.Request.Method,
                    httpContext.Request.Path,
                    ApiErrorWriter.TraceIdOf(httpContext));
                return new ApiErrorShape(ErrorType.Unexpected, ErrorCodes.InternalError, "Something went wrong on our side. Please try again later.");
        }
    }

    private static string ToCamelCase(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(part =>
            part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
