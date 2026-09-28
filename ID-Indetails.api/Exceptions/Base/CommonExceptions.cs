using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Exceptions.Base;

/// <summary>401. Not signed in, or credentials/tokens are not valid.</summary>
public sealed class UnauthenticatedException(string code, string message)
    : AppException(ErrorType.Authentication, code, message);

/// <summary>403. Signed in but not allowed.</summary>
public sealed class ForbiddenException(string code, string message)
    : AppException(ErrorType.Authorization, code, message);

/// <summary>404. Missing, or not visible to the caller (another domain's records read as not found).</summary>
public sealed class NotFoundException(string code, string message)
    : AppException(ErrorType.NotFound, code, message)
{
    public NotFoundException(string message) : this(ErrorCodes.NotFound, message) { }
}

/// <summary>409. Conflicts with existing data.</summary>
public sealed class ConflictException(string code, string message)
    : AppException(ErrorType.Conflict, code, message);

/// <summary>422. A business rule forbids the operation in the current state.</summary>
public sealed class BusinessRuleException(string code, string message)
    : AppException(ErrorType.BusinessRule, code, message);

/// <summary>400. A field-level validation failure detected in the service layer.</summary>
public sealed class RequestValidationException(string code, string field, string message)
    : AppException(
        ErrorType.Validation,
        code,
        message,
        new Dictionary<string, string[]> { [field] = [message] });

/// <summary>503. A dependency needed to finish the operation is unavailable.</summary>
public sealed class ServiceUnavailableException(string code, string message, Exception? innerException = null)
    : AppException(ErrorType.Unavailable, code, message, null, innerException);
