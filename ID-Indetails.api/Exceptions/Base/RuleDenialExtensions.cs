using ID_Indetails.api.Business.Common;

namespace ID_Indetails.api.Exceptions.Base;

public static class RuleDenialExtensions
{
    /// <summary>The exception that reports <paramref name="denial"/> through the uniform error contract.</summary>
    public static AppException ToException(this RuleDenial denial) => denial.Kind switch
    {
        DenialKind.Forbidden => new ForbiddenException(denial.Code, denial.Message),
        DenialKind.Validation => new RequestValidationException(denial.Code, denial.Field ?? "request", denial.Message),
        _ => new BusinessRuleException(denial.Code, denial.Message),
    };

    public static void ThrowIfDenied(this RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
