using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Files;
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
/// <item>A message carries at most one voice note (an audio file) among its attachments.</item>
/// <item>Whoever sent a message can delete it, whatever it carries. A shared work-entry comment stays: it belongs to the comment. So does a custom group's grey line, the record of what changed.</item>
/// <item>Custom groups (decided 24 November 2026) have the members a holder of <c>message:manage_groups</c> picked:
/// any active staff, from any business. Only members read and write in one, and someone taken out loses it, history
/// included. Mentions there are of its members.</item>
/// </list>
/// Team chats and direct chats mirror the organisation, so they follow role and team rather than permissions.
/// </summary>
public static class MessagingRules
{
    public const int MaxBodyLength = 4000;
    public const int MaxMentions = 50;
    public const int MaxAttachments = 10;
    public const int MaxGroupNameLength = 80;
    public const int MaxGroupMembers = 200;

    /// <summary>
    /// Everyone to put in a group must be active staff: <paramref name="found"/> are the people of
    /// <paramref name="userIds"/> that exist. Portal clients aren't staff, so they are never among them.
    /// </summary>
    public static RuleDenial? CheckGroupMembers(IReadOnlyCollection<long> userIds, IReadOnlyCollection<Person> found)
    {
        var active = found.Where(p => p.Status == UserStatus.Active).Select(p => p.Id).ToHashSet();
        return userIds.All(active.Contains)
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "memberIds", "Only active staff can be in a group.");
    }

    /// <summary>A group holds at most <see cref="MaxGroupMembers"/> people.</summary>
    public static RuleDenial? CheckGroupSize(int members)
        => members <= MaxGroupMembers
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "memberIds", $"A group has at most {MaxGroupMembers} people.");

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

    /// <param name="attachmentTypes">The stored type (MIME) of each file attached to the message.</param>
    public static RuleDenial? CheckVoiceNotes(IEnumerable<string> attachmentTypes)
        => attachmentTypes.Count(type => FileRules.KindOf(type) == FileKind.Audio) > 1
            ? RuleDenial.BusinessRule(ErrorCodes.TooManyVoiceNotes, "A message can carry one voice note.")
            : null;

    public static RuleDenial? CheckDelete(long senderId, long callerId, string kind)
    {
        if (senderId != callerId)
        {
            return RuleDenial.Forbidden(ErrorCodes.Forbidden, "Only the person who sent a message can delete it.");
        }

        return kind switch
        {
            MessageKinds.Text => null,
            MessageKinds.GroupEvent => RuleDenial.BusinessRule(ErrorCodes.MessageNotDeletable, "A group's grey line can't be deleted: it says what changed in the group."),
            _ => RuleDenial.BusinessRule(ErrorCodes.MessageNotDeletable, "A shared work comment can't be deleted from the chat."),
        };
    }

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
