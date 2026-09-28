using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Exceptions.Base;

/// <summary>
/// An expected failure whose <see cref="Exception.Message"/> is safe to show the client.
/// The global exception handler turns it into the uniform error response. Anything that
/// is not an <see cref="AppException"/> is treated as unexpected and its details are hidden.
/// </summary>
public class AppException : Exception
{
    public AppException(
        ErrorType type,
        string code,
        string message,
        IDictionary<string, string[]>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Type = type;
        Code = code;
        Errors = errors;
    }

    public ErrorType Type { get; }

    public string Code { get; }

    /// <summary>Optional field errors, for validation failures found in the service layer.</summary>
    public IDictionary<string, string[]>? Errors { get; }
}
