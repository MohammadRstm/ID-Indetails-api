using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Error;

/// <summary>
/// The single error shape every endpoint returns, whatever went wrong.
/// </summary>
public sealed class ApiErrorResponse
{
    public ErrorType Type { get; init; }

    /// <summary>Machine-readable code (see <c>Constants/ErrorCodes</c>).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Safe, human-readable message. Never contains internal details.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Field errors, keyed by camelCase field name. Only for validation errors.</summary>
    public IDictionary<string, string[]>? Errors { get; init; }

    /// <summary>
    /// For <c>profit_floor</c>: the refused change, kept so whoever sees the project's money can force it through.
    /// </summary>
    public long? BlockedChangeId { get; init; }

    /// <summary>Correlates the response with server logs.</summary>
    public string? TraceId { get; init; }
}
