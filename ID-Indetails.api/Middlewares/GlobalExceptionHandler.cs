using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// The one place exceptions become HTTP responses.
/// <list type="bullet">
/// <item><see cref="ValidationException"/> (FluentValidation): 400 with field errors.</item>
/// <item>A number too large for its database column: 400, said in words.</item>
/// <item><see cref="AppException"/>: its own status, code and client-safe message.</item>
/// <item>Anything else: logged in full, and the client gets a generic 500 with only a trace id.</item>
/// </list>
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

                await ApiErrorWriter.WriteAsync(
                    httpContext,
                    ErrorType.Validation,
                    ErrorCodes.ValidationFailed,
                    "One or more fields are invalid.",
                    errors,
                    cancellationToken);
                return true;

            // A number the validators let through that still doesn't fit its column (a sum, a product, a
            // conversion): the client's request, not a server fault, said in words.
            case DbUpdateException { InnerException: MySqlException { ErrorCode: MySqlErrorCode.WarningDataOutOfRange } }:
                logger.LogWarning(exception, "A value too large for its column. TraceId {TraceId}", ApiErrorWriter.TraceIdOf(httpContext));

                await ApiErrorWriter.WriteAsync(
                    httpContext,
                    ErrorType.Validation,
                    ErrorCodes.ValidationFailed,
                    "A number here is too large to be saved.",
                    cancellationToken: cancellationToken);
                return true;

            case AppException app:
                // Expected outcome of a request, not a server fault: no stack trace needed.
                logger.LogInformation(
                    "Request failed with {ErrorType}/{ErrorCode}: {ErrorMessage}",
                    app.Type, app.Code, app.Message);

                await ApiErrorWriter.WriteAsync(
                    httpContext, app.Type, app.Code, app.Message, app.Errors, cancellationToken);
                return true;

            default:
                logger.LogError(
                    exception,
                    "Unhandled exception for {Method} {Path}. TraceId {TraceId}",
                    httpContext.Request.Method,
                    httpContext.Request.Path,
                    ApiErrorWriter.TraceIdOf(httpContext));

                await ApiErrorWriter.WriteAsync(
                    httpContext,
                    ErrorType.Unexpected,
                    ErrorCodes.InternalError,
                    "Something went wrong on our side. Please try again later.",
                    cancellationToken: cancellationToken);
                return true;
        }
    }

    private static string ToCamelCase(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(part =>
            part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
