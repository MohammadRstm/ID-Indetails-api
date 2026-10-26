using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Execution;

/// <summary>
/// Who oversees an Execution site, and their daily logs: staff are active Execution employees, every site has at least
/// one, and a log can be changed only on the company day it was written.
/// </summary>
public static class SiteTeamRules
{
    public const int MaxLogLength = 2000;

    /// <summary>A person being put on a site.</summary>
    public sealed record Candidate(long Id, string Name, string Role, string? Domain, UserStatus Status);

    /// <summary>The staff chosen for a site: at least one, each an active Execution employee, each once.</summary>
    public static RuleDenial? CheckStaff(IReadOnlyList<long> chosen, IReadOnlyCollection<Candidate> found)
    {
        if (chosen.Count == 0)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "staffIds", "Choose at least one staff member to oversee the site.");
        }

        if (chosen.Distinct().Count() != chosen.Count)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "staffIds", "Choose each staff member once.");
        }

        if (chosen.Any(id => found.All(c => c.Id != id)))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "staffIds", "A chosen staff member doesn't exist.");
        }

        var wrong = found.FirstOrDefault(c => c.Role != RoleCodes.Employee || c.Domain != DomainCodes.Execution || c.Status != UserStatus.Active);
        return wrong is null
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "staffIds", $"{wrong.Name} isn't an active Execution employee, so they can't oversee a site.");
    }

    /// <summary>Only a site's own staff report its labour and write its logs; the manage permission reports any.</summary>
    public static RuleDenial? CheckOnTeam(bool onTeam)
        => onTeam ? null : RuleDenial.Forbidden(ErrorCodes.NotOnSiteTeam, "Only this site's staff record here. Ask an admin to add you to it.");

    /// <summary>The writer changes a log on the company day it was written, until midnight; after that it stands.</summary>
    public static bool IsEditable(DateOnly writtenOn, DateOnly today) => writtenOn == today;

    public static RuleDenial? CheckEditable(DateOnly writtenOn, DateOnly today)
        => IsEditable(writtenOn, today)
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.SiteLogLocked, "A log can only be changed on the day it was written. Write a new one instead.");
}
