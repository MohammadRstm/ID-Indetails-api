namespace ID_Indetails.api.Enums;

/// <summary>
/// Category of an API error. Decides the HTTP status and is returned to the client as
/// <c>type</c> in the uniform error response.
/// </summary>
public enum ErrorType
{
    /// <summary>400: the request is malformed or fails validation.</summary>
    Validation,

    /// <summary>401: not signed in, or the credentials/token are not valid.</summary>
    Authentication,

    /// <summary>403: signed in, but not allowed to do this.</summary>
    Authorization,

    /// <summary>404: the resource does not exist or is not visible to the caller.</summary>
    NotFound,

    /// <summary>409: conflicts with existing data (e.g. email already registered).</summary>
    Conflict,

    /// <summary>422: valid request, but a business rule forbids it in the current state.</summary>
    BusinessRule,

    /// <summary>429: too many requests.</summary>
    RateLimited,

    /// <summary>503: a dependency (e.g. email delivery) is unavailable.</summary>
    Unavailable,

    /// <summary>500: an unexpected server error. Never carries internal details.</summary>
    Unexpected,
}
