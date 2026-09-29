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
}
