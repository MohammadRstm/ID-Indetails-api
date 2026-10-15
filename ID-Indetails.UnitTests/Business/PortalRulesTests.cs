using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PortalRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

    private static PortalProjectFacts Project(
        BillingType billing = BillingType.Client, long? clientId = 7, ProjectStatus status = ProjectStatus.Active, bool clientArchived = false)
        => new(1, DomainCodes.Design, "Villa", status, billing, clientId, clientArchived, [new PortalClientContact("rita@client.test", "Office")]);

    [Fact]
    public void A_link_is_usable_until_it_is_used_cancelled_or_expired()
    {
        PortalRules.StateOf(null, null, Now.AddHours(1), PortalAccountStatus.Invited, Now).ShouldBe(PortalInvitationState.Usable);
        PortalRules.StateOf(null, null, Now, PortalAccountStatus.Invited, Now).ShouldBe(PortalInvitationState.Expired);
        PortalRules.StateOf(null, Now.AddMinutes(-5), Now.AddHours(1), PortalAccountStatus.Invited, Now).ShouldBe(PortalInvitationState.Revoked);
        PortalRules.StateOf(Now.AddMinutes(-5), null, Now.AddHours(1), PortalAccountStatus.Active, Now).ShouldBe(PortalInvitationState.Used);
    }

    [Fact]
    public void A_link_of_an_account_already_set_up_counts_as_used_even_if_cancelled_or_expired()
    {
        PortalRules.StateOf(null, null, Now.AddHours(1), PortalAccountStatus.Active, Now).ShouldBe(PortalInvitationState.Used);
        PortalRules.StateOf(null, Now.AddMinutes(-5), Now.AddHours(-1), PortalAccountStatus.Active, Now).ShouldBe(PortalInvitationState.Used);
    }

    [Fact]
    public void Only_open_client_projects_of_a_current_client_can_be_invited_to()
    {
        PortalRules.CheckInvitable(Project()).ShouldBeNull();
        PortalRules.CheckInvitable(Project(status: ProjectStatus.Completed)).ShouldBeNull();
        PortalRules.CheckInvitable(Project(status: ProjectStatus.Hold)).ShouldBeNull();
        PortalRules.CheckInvitable(Project(billing: BillingType.Internal, clientId: null))!.Code.ShouldBe(ErrorCodes.PortalProjectNotEligible);
        PortalRules.CheckInvitable(Project(status: ProjectStatus.Archived))!.Code.ShouldBe(ErrorCodes.PortalProjectNotEligible);
        PortalRules.CheckInvitable(Project(clientArchived: true))!.Code.ShouldBe(ErrorCodes.PortalProjectNotEligible);
    }

    [Fact]
    public void Only_the_clients_own_email_addresses_can_be_invited()
    {
        PortalRules.IsClientContact(Project().EmailContacts, "rita@client.test").ShouldBeTrue();
        PortalRules.IsClientContact(Project().EmailContacts, "someone@else.test").ShouldBeFalse();
    }

    [Fact]
    public void An_invitation_is_accepted_only_while_the_project_would_still_take_the_address()
    {
        PortalRules.CanAccept(Project(), "rita@client.test").ShouldBeTrue();
        PortalRules.CanAccept(Project(status: ProjectStatus.Completed), "rita@client.test").ShouldBeTrue();
        PortalRules.CanAccept(Project(), "someone@else.test").ShouldBeFalse();
        PortalRules.CanAccept(Project(status: ProjectStatus.Archived), "rita@client.test").ShouldBeFalse();
        PortalRules.CanAccept(Project(clientArchived: true), "rita@client.test").ShouldBeFalse();
        PortalRules.CanAccept(Project(billing: BillingType.Internal, clientId: null), "rita@client.test").ShouldBeFalse();
        PortalRules.CanAccept(Project() with { DomainCode = DomainCodes.Execution }, "rita@client.test").ShouldBeFalse();
    }

    [Fact]
    public void A_paused_sign_in_stays_paused_until_its_time()
    {
        PortalRules.IsLocked(Now.AddMinutes(1), Now).ShouldBeTrue();
        PortalRules.IsLocked(Now, Now).ShouldBeFalse();
        PortalRules.IsLocked(null, Now).ShouldBeFalse();
    }

    [Fact]
    public void A_contact_reads_as_active_invited_expired_ended_or_not_invited()
    {
        PortalRules.StatusOf(PortalAccountStatus.Active, null, true, Now, null, Now, Now).ShouldBe(PortalClientStatus.Active);
        PortalRules.StatusOf(PortalAccountStatus.Invited, null, false, null, null, Now.AddHours(2), Now).ShouldBe(PortalClientStatus.Invited);
        PortalRules.StatusOf(PortalAccountStatus.Invited, null, false, null, null, Now.AddHours(-2), Now).ShouldBe(PortalClientStatus.InvitationExpired);
        PortalRules.StatusOf(PortalAccountStatus.Active, Now, true, Now, null, Now, Now).ShouldBe(PortalClientStatus.AccessEnded);
        PortalRules.StatusOf(PortalAccountStatus.Invited, null, false, null, Now, Now.AddHours(2), Now).ShouldBe(PortalClientStatus.NotInvited);
        PortalRules.StatusOf(null, null, false, null, null, null, Now).ShouldBe(PortalClientStatus.NotInvited);
    }

    [Fact]
    public void A_project_has_news_when_published_since_last_opened_or_never_opened_or_anything_is_unread()
    {
        // nothing published, nothing unread
        PortalRules.HasNews(null, null, 0).ShouldBeFalse();
        PortalRules.HasNews(null, Now, 0).ShouldBeFalse();

        // published and never opened, or published after the last visit
        PortalRules.HasNews(Now, null, 0).ShouldBeTrue();
        PortalRules.HasNews(Now, Now.AddMinutes(-1), 0).ShouldBeTrue();

        // opened since the last publication
        PortalRules.HasNews(Now, Now, 0).ShouldBeFalse();
        PortalRules.HasNews(Now, Now.AddMinutes(1), 0).ShouldBeFalse();

        // unread messages or comments, whatever was opened
        PortalRules.HasNews(Now, Now.AddMinutes(1), 2).ShouldBeTrue();
        PortalRules.HasNews(null, null, 1).ShouldBeTrue();
    }

    [Fact]
    public void A_projects_latest_activity_is_the_latest_moment_there_is()
    {
        PortalRules.LatestOf(Now, Now.AddHours(2), Now.AddHours(1)).ShouldBe(Now.AddHours(2));
        PortalRules.LatestOf(null, Now, null).ShouldBe(Now);
        PortalRules.LatestOf(null, null, null).ShouldBeNull();
    }
}
