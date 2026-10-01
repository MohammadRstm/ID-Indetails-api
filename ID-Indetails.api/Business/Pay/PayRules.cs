using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Pay;

public static class PayRules
{
    /// <summary>Average working hours in a month (40 h × 52 weeks ÷ 12), as used by the prototype.</summary>
    public const decimal HoursPerMonth = 173.33m;

    /// <summary>
    /// Internal hourly cost used to cost project labour: the rate itself for hourly pay,
    /// salary ÷ <see cref="HoursPerMonth"/> for monthly pay (4 decimal places).
    /// </summary>
    public static decimal HourlyCostRate(PayBasis basis, decimal amount) => basis switch
    {
        PayBasis.Hourly => amount,
        PayBasis.Monthly => Math.Round(amount / HoursPerMonth, 4, MidpointRounding.AwayFromZero),
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown pay basis."),
    };

    /// <summary>Super admins are paid a monthly salary: their pay never depends on hours, so it can't be hourly.</summary>
    public static RuleDenial? CheckBasis(string role, PayBasis basis)
        => role == RoleCodes.SuperAdmin && basis == PayBasis.Hourly
            ? RuleDenial.Invalid(ErrorCodes.SuperAdminPaidMonthly, "basis", "A super admin is paid a monthly salary: their pay doesn't depend on hours.")
            : null;
}
