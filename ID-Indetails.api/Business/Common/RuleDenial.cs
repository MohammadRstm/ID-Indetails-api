namespace ID_Indetails.api.Business.Common;

/// <summary>How a refused operation is reported: 403, 422 or 400.</summary>
public enum DenialKind
{
    Forbidden,
    BusinessRule,
    Validation,
}

/// <summary>
/// Why a business rule refused an operation. Business logic returns it instead of throwing,
/// so it stays free of HTTP concerns; services turn it into the matching exception.
/// </summary>
/// <param name="Field">The request field at fault, for <see cref="DenialKind.Validation"/>.</param>
public sealed record RuleDenial(DenialKind Kind, string Code, string Message, string? Field = null)
{
    public static RuleDenial Forbidden(string code, string message) => new(DenialKind.Forbidden, code, message);

    public static RuleDenial BusinessRule(string code, string message) => new(DenialKind.BusinessRule, code, message);

    public static RuleDenial Invalid(string code, string field, string message) => new(DenialKind.Validation, code, message, field);
}
