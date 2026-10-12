namespace ID_Indetails.api.Enums;

/// <summary>
/// What kind of harm a permission's actions could do, for Proton: an action needing a tagged permission must be in the
/// matching family and confirmed strongly (see Proton/Actions/ProtonCoverage). Code only: it is not stored or seeded.
/// </summary>
public enum PermissionSensitivity
{
    None,
    /// <summary>Records, moves or reveals money: invoices, payments, costs, prices, overhead.</summary>
    Money,
    /// <summary>Decides or reveals what people are paid: pay, salaries, hours, schedules, the working week.</summary>
    Pay,
    /// <summary>Decides who can sign in and what they can do.</summary>
    Access,
    /// <summary>Rules that apply to everyone in the company or a domain.</summary>
    CompanyRules,
}
