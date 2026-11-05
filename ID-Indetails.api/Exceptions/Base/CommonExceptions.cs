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

/// <summary>
/// 422. A change kept instead of made: once its transaction has rolled back, the error handler keeps it as a blocked
/// change (<see cref="Id"/>) and tells whoever decides it.
/// </summary>
public abstract class BlockedChangeException(
    string code, string message, long projectId, string projectTitle, string domain, BlockedChangeKind kind, string payload, long requestedById)
    : AppException(ErrorType.BusinessRule, code, message)
{
    public long ProjectId { get; } = projectId;

    public string ProjectTitle { get; } = projectTitle;

    public string Domain { get; } = domain;

    public BlockedChangeKind Kind { get; } = kind;

    /// <summary>The refused request, as JSON with its route ids.</summary>
    public string Payload { get; } = payload;

    public long RequestedById { get; } = requestedById;

    /// <summary>The blocked change it was kept as; set once recorded.</summary>
    public long? Id { get; set; }
}

/// <summary>
/// 422 <c>profit_floor</c>. The change would take the project's profit to zero or below. Kept for whoever sees the
/// project's money, who can force it through.
/// </summary>
public sealed class ProfitFloorException(long projectId, string projectTitle, string domain, BlockedChangeKind kind, string payload, long requestedById)
    : BlockedChangeException(ErrorCodes.ProfitFloor,
        "This would take the project's profit to zero or below, so it wasn't saved. Whoever sees the project's money was asked to look.",
        projectId, projectTitle, domain, kind, payload, requestedById);

/// <summary>
/// 422 <c>department_not_on_project</c>. Hours logged by someone whose department isn't on the project: kept, and they
/// go through once the department is added. Its owner and the super admins are told.
/// </summary>
public sealed class DepartmentBlockedException(
    long projectId, string projectTitle, string domain, string payload, long requestedById, long ownerId, long teamId, string departmentName, string personName)
    : BlockedChangeException(ErrorCodes.DepartmentNotOnProject,
        $"{departmentName} isn't one of {projectTitle}'s departments yet, so your hours are kept until it is added. "
        + "Its owner and the super admins were told; you'll hear when they go through, so don't log them again.",
        projectId, projectTitle, domain, BlockedChangeKind.WorkEntryDepartment, payload, requestedById)
{
    public long OwnerId { get; } = ownerId;

    public long TeamId { get; } = teamId;

    public string DepartmentName { get; } = departmentName;

    public string PersonName { get; } = personName;
}
