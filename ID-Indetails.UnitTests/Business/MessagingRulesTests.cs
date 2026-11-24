using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Messaging;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using P = ID_Indetails.api.Business.Messaging.MessagingRules.Person;

namespace ID_Indetails.UnitTests.Business;

public class MessagingRulesTests
{
    private const long Engineering = 1;
    private const long Data = 3;
    private const long SiteOperations = 7;

    private static readonly P Super = new(1, RoleCodes.SuperAdmin, null, null, UserStatus.Active);
    private static readonly P DesignAdmin = new(2, RoleCodes.Admin, DomainCodes.Design, null, UserStatus.Active);
    private static readonly P Engineer = new(3, RoleCodes.Employee, DomainCodes.Design, Engineering, UserStatus.Active);
    private static readonly P Analyst = new(4, RoleCodes.Employee, DomainCodes.Design, Data, UserStatus.Active);
    private static readonly P SiteWorker = new(5, RoleCodes.Employee, DomainCodes.Execution, SiteOperations, UserStatus.Active);
    private static readonly P ExecutionAdmin = new(6, RoleCodes.Admin, DomainCodes.Execution, null, UserStatus.Active);

    [Fact]
    public void A_team_chat_is_the_team_its_domains_admins_and_the_super_admins()
    {
        var engineering = new MessagingRules.TeamChat(Engineering, DomainCodes.Design);

        MessagingRules.IsMember(Engineer, engineering).ShouldBeTrue();
        MessagingRules.IsMember(DesignAdmin, engineering).ShouldBeTrue();
        MessagingRules.IsMember(Super, engineering).ShouldBeTrue();
        MessagingRules.IsMember(Analyst, engineering).ShouldBeFalse();
        MessagingRules.IsMember(ExecutionAdmin, engineering).ShouldBeFalse();
        MessagingRules.IsMember(SiteWorker, new MessagingRules.TeamChat(SiteOperations, DomainCodes.Execution)).ShouldBeTrue();
    }

    [Fact]
    public void People_message_their_own_domain_and_the_super_admins_but_never_themselves()
    {
        MessagingRules.CanReach(Engineer, Analyst).ShouldBeTrue();
        MessagingRules.CanReach(Engineer, Super).ShouldBeTrue();
        MessagingRules.CanReach(Super, SiteWorker).ShouldBeTrue();
        MessagingRules.CanReach(Engineer, SiteWorker).ShouldBeFalse();
        MessagingRules.CanReach(DesignAdmin, ExecutionAdmin).ShouldBeFalse();
        MessagingRules.CanReach(Engineer, Engineer).ShouldBeFalse();
    }

    [Fact]
    public void Only_active_people_receive_messages()
    {
        MessagingRules.CheckRecipient(Analyst).ShouldBeNull();
        MessagingRules.CheckRecipient(Analyst with { Status = UserStatus.Deactivated })!.Code.ShouldBe(ErrorCodes.UserDeactivated);
        MessagingRules.CheckRecipient(Analyst with { Status = UserStatus.Invited })!.Kind.ShouldBe(DenialKind.BusinessRule);
    }

    [Fact]
    public void Mentions_are_for_group_chats_and_their_members_only()
    {
        var members = new HashSet<long> { Engineer.Id, DesignAdmin.Id };

        MessagingRules.CheckMentions([], null).ShouldBeNull();
        MessagingRules.CheckMentions([Engineer.Id], members).ShouldBeNull();
        MessagingRules.CheckMentions([Analyst.Id], members)!.Field.ShouldBe("mentionIds");
        MessagingRules.CheckMentions([Engineer.Id], null)!.Field.ShouldBe("mentionIds");
    }

    [Fact]
    public void A_message_carries_at_most_one_voice_note()
    {
        MessagingRules.CheckVoiceNotes([]).ShouldBeNull();
        MessagingRules.CheckVoiceNotes(["audio/webm"]).ShouldBeNull();
        MessagingRules.CheckVoiceNotes(["audio/webm", "image/png", "video/mp4"]).ShouldBeNull();

        var denial = MessagingRules.CheckVoiceNotes(["audio/webm", "audio/ogg"])!;
        denial.Code.ShouldBe(ErrorCodes.TooManyVoiceNotes);
        denial.Message.ShouldBe("A message can carry one voice note.");
    }

    [Fact]
    public void Only_the_sender_deletes_and_only_a_voice_message()
    {
        MessagingRules.CheckDelete(7, 7, MessageKinds.Text, ["audio/webm"]).ShouldBeNull();
        MessagingRules.CheckDelete(7, 7, MessageKinds.Text, ["audio/ogg", "image/png"]).ShouldBeNull();

        var someoneElse = MessagingRules.CheckDelete(7, 8, MessageKinds.Text, ["audio/webm"])!;
        (someoneElse.Kind, someoneElse.Code).ShouldBe((DenialKind.Forbidden, ErrorCodes.Forbidden));
        someoneElse.Message.ShouldBe("Only the person who sent a voice message can delete it.");

        foreach (var (kind, types) in new (string Kind, string[] Types)[] { (MessageKinds.Text, []), (MessageKinds.Text, ["image/png"]), (MessageKinds.TaskComment, ["audio/webm"]) })
        {
            MessagingRules.CheckDelete(7, 7, kind, types)!.Code.ShouldBe(ErrorCodes.MessageNotDeletable);
        }

        // a group's grey line isn't a voice message either
        MessagingRules.CheckDelete(7, 7, MessageKinds.GroupEvent, [])!.Code.ShouldBe(ErrorCodes.MessageNotDeletable);
    }

    [Fact]
    public void Anyone_active_on_staff_of_any_business_can_be_put_in_a_group()
    {
        P[] everyone = [Super, DesignAdmin, Engineer, SiteWorker, ExecutionAdmin];
        MessagingRules.CheckGroupMembers([Super.Id, Engineer.Id, SiteWorker.Id, ExecutionAdmin.Id], everyone).ShouldBeNull();
        MessagingRules.CheckGroupMembers([], everyone).ShouldBeNull();

        var gone = Analyst with { Status = UserStatus.Deactivated };
        var deactivated = MessagingRules.CheckGroupMembers([Engineer.Id, gone.Id], [Engineer, gone])!;
        (deactivated.Kind, deactivated.Code, deactivated.Field).ShouldBe((DenialKind.Validation, ErrorCodes.ValidationFailed, "memberIds"));
        deactivated.Message.ShouldBe("Only active staff can be in a group.");

        // someone who doesn't exist (a portal client's account is no member of staff)
        MessagingRules.CheckGroupMembers([Engineer.Id, 999], [Engineer])!.Field.ShouldBe("memberIds");
    }

    [Fact]
    public void A_group_holds_at_most_two_hundred_people()
    {
        MessagingRules.CheckGroupSize(MessagingRules.MaxGroupMembers).ShouldBeNull();
        MessagingRules.CheckGroupSize(MessagingRules.MaxGroupMembers + 1)!.Message.ShouldBe("A group has at most 200 people.");
    }

    [Fact]
    public void A_groups_grey_lines_name_what_changed()
    {
        GroupEvents.Created("Site leads").ShouldBe("created the group “Site leads”");
        GroupEvents.Renamed("Leads").ShouldBe("renamed the group to “Leads”");
        GroupEvents.Added(["Sara Haddad"]).ShouldBe("added Sara Haddad");
        GroupEvents.Added(["Ali Nasser", "Sara Haddad"]).ShouldBe("added Ali Nasser and Sara Haddad");
        GroupEvents.Added(["Ali Nasser", "Rana Aoun", "Sara Haddad"]).ShouldBe("added Ali Nasser, Rana Aoun and Sara Haddad");
        GroupEvents.Removed("Sara Haddad").ShouldBe("removed Sara Haddad");
        GroupEvents.Left.ShouldBe("left the group");
    }
}
