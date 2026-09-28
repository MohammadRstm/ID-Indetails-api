using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Execution;

/// <summary>What every record kept on an Execution site shares (materials, ground labour).</summary>
public static class SiteRules
{
    /// <summary>
    /// Only Execution projects keep site records, and a finalised project is locked: nobody, admins
    /// included, changes them until it is reopened. Projects on hold stay open.
    /// </summary>
    public static RuleDenial? CheckOpen(string domain, ProjectStatus status, bool revenueLocked)
    {
        if (domain != DomainCodes.Execution)
        {
            return RuleDenial.BusinessRule(ErrorCodes.NotForThisDomain, "Only Execution projects keep site records.");
        }

        return status is ProjectStatus.Completed or ProjectStatus.Archived || revenueLocked
            ? RuleDenial.BusinessRule(ErrorCodes.ProjectLocked, "This project is finalised. Reopen it to change what was recorded on site.")
            : null;
    }

    /// <summary>People change what they recorded themselves; the manage permission changes anyone's.</summary>
    public static bool CanChange(bool isOwn, bool canManageAll) => isOwn || canManageAll;

    public static RuleDenial? CheckCanChange(bool isOwn, bool canManageAll)
        => CanChange(isOwn, canManageAll)
            ? null
            : RuleDenial.Forbidden(ErrorCodes.Forbidden, "You can only change what you recorded yourself.");
}
