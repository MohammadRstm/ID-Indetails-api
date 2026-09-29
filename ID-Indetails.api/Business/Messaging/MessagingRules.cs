using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Messaging;

/// <summary>
/// Who talks to whom, as in the prototype (75-messages-data.js):
/// <list type="bullet">
/// <item>Every team has one group chat. Its members are worked out live, never stored: the team's people, the admins
/// of the team's domain, and the super admins. Moving someone changes the chats they see, history included.</item>
/// <item>So employees see their own team's chat, admins every team chat of their domain, super admins all of them.</item>
/// <item>Super admins message anyone directly; everyone else messages their own domain and the super admins, never the
/// other domain. Nobody messages themselves, and only active people receive messages.</item>
/// <item>Mentions exist only in group chats, and only of the chat's members.</item>
/// </list>
/// Chats mirror the organisation, so they follow role and team rather than permissions.
/// </summary>
public static class MessagingRules
{
    public const int MaxBodyLength = 4000;
    public const int MaxMentions = 50;
    public const int MaxAttachments = 10;

    public sealed record Person(long Id, string Role, string? Domain, long? TeamId, UserStatus Status);

    public sealed record TeamChat(long TeamId, string Domain);

    public static bool IsMember(Person person, TeamChat chat) => person.Role switch
    {
        RoleCodes.SuperAdmin => true,
        RoleCodes.Admin => person.Domain == chat.Domain,
        _ => person.Domain == chat.Domain && person.TeamId == chat.TeamId,
    };

    public static bool CanReach(Person from, Person to)
        => from.Id != to.Id && (from.Role == RoleCodes.SuperAdmin || to.Role == RoleCodes.SuperAdmin || from.Domain == to.Domain);

    public static RuleDenial? CheckRecipient(Person to)
        => to.Status == UserStatus.Active
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.UserDeactivated, "Only active people can receive messages.");

    public static RuleDenial? CheckMentions(IReadOnlyCollection<long> mentionIds, IReadOnlySet<long>? memberIds)
    {
        if (mentionIds.Count == 0)
        {
            return null;
        }

        if (memberIds is null)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "mentionIds", "Mentions are only for group chats.");
        }

        return mentionIds.All(memberIds.Contains)
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "mentionIds", "Only people in this chat can be mentioned.");
    }
}
