using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PortalDiscussionRulesTests
{
    private static readonly DiscussionPerson Nadia = new(DiscussionSide.Staff, 2, "Nadia Karam");
    private static readonly DiscussionPerson Sami = new(DiscussionSide.Staff, 7, "Sami Aoun");
    private static readonly DiscussionPerson Rita = new(DiscussionSide.Client, 2, "Rita Frem");

    private static DiscussionProject Project(
        string domain = DomainCodes.Design, BillingType billing = BillingType.Client, long? clientId = 4, ProjectStatus status = ProjectStatus.Active)
        => new(1, domain, "Villa", status, billing, clientId, OwnerId: 2);

    [Fact]
    public void Staff_and_clients_with_the_same_id_are_different_people()
    {
        Nadia.Key.ShouldBe("u2");
        Rita.Key.ShouldBe("c2");
        Nadia.Key.ShouldNotBe(Rita.Key);
    }

    [Fact]
    public void Only_Design_client_projects_have_a_discussion()
    {
        PortalDiscussionRules.HasDiscussion(Project()).ShouldBeTrue();
        PortalDiscussionRules.HasDiscussion(Project(domain: DomainCodes.Execution)).ShouldBeFalse();
        PortalDiscussionRules.HasDiscussion(Project(billing: BillingType.Internal, clientId: null)).ShouldBeFalse();
    }

    [Fact]
    public void An_archived_projects_discussion_is_read_only_and_a_completed_ones_is_not()
    {
        PortalDiscussionRules.CheckWritable(ProjectStatus.Active).ShouldBeNull();
        PortalDiscussionRules.CheckWritable(ProjectStatus.Completed).ShouldBeNull();
        PortalDiscussionRules.CheckWritable(ProjectStatus.Archived)!.Code.ShouldBe(ErrorCodes.PortalDiscussionClosed);
    }

    [Fact]
    public void Only_people_taking_part_can_be_mentioned_and_mentioning_yourself_does_nothing()
    {
        DiscussionPerson[] everyone = [Nadia, Sami, Rita];

        PortalDiscussionRules.Mentioned(["u7", "c2", "u7"], everyone, Nadia)!.Select(p => p.Key).ShouldBe(["u7", "c2"]);
        PortalDiscussionRules.Mentioned(["u2"], everyone, Nadia)!.ShouldBeEmpty();
        PortalDiscussionRules.Mentioned(["u99"], everyone, Nadia).ShouldBeNull();
        PortalDiscussionRules.Mentioned(["c7"], everyone, Nadia).ShouldBeNull();
    }

    [Fact]
    public void A_read_marker_only_moves_forward_and_never_past_what_exists()
    {
        PortalDiscussionRules.MoveMarker(null, 5, 10).ShouldBe(5);
        PortalDiscussionRules.MoveMarker(8, 5, 10).ShouldBe(8);
        PortalDiscussionRules.MoveMarker(8, 50, 10).ShouldBe(10);
        PortalDiscussionRules.MoveMarker(8, null, 10).ShouldBe(8);
        PortalDiscussionRules.MoveMarker(null, 5, null).ShouldBeNull();
    }

    [Fact]
    public void Text_is_trimmed_and_pages_stay_within_bounds()
    {
        PortalDiscussionRules.Clean("  Hello\r\nthere  ").ShouldBe("Hello\nthere");
        PortalDiscussionRules.PageSize(null).ShouldBe(30);
        PortalDiscussionRules.PageSize(500).ShouldBe(50);
        PortalDiscussionRules.PageSize(0).ShouldBe(1);
    }
}
