using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PortalPublishRulesTests
{
    private const PortalSection P = PortalSection.Presentation;
    private const PortalSection M = PortalSection.Model;

    /// <summary>An item chosen for the client and published as it is now, unless told otherwise.</summary>
    private static PortalItemCopies Item(
        long id, int order, PortalSection section = P, long? file = null, string title = "Title",
        bool published = true, long? publishedFile = null, string? publishedTitle = null, PortalSection? publishedSection = null,
        int? publishedOrder = null, bool removed = false, bool forClient = true)
    {
        var fileId = file ?? id * 10;
        return published
            ? new(id, fileId, title, section, order, publishedFile ?? fileId, publishedTitle ?? title, publishedSection ?? section, publishedOrder ?? order, removed, forClient)
            : new(id, fileId, title, section, order, null, null, null, null, false, forClient);
    }

    [Fact]
    public void An_item_is_new_changed_unchanged_or_removed()
    {
        PortalPublishRules.StateOf(Item(1, 1024, published: false)).ShouldBe(PortalItemState.New);
        PortalPublishRules.StateOf(Item(1, 1024)).ShouldBe(PortalItemState.Unchanged);
        PortalPublishRules.StateOf(Item(1, 1024, publishedFile: 99)).ShouldBe(PortalItemState.Changed);
        PortalPublishRules.StateOf(Item(1, 1024, publishedTitle: "Old")).ShouldBe(PortalItemState.Changed);
        PortalPublishRules.StateOf(Item(1, 1024, section: M, publishedSection: P)).ShouldBe(PortalItemState.Changed);
        PortalPublishRules.StateOf(Item(1, 1024, removed: true)).ShouldBe(PortalItemState.Removed);
    }

    [Fact]
    public void A_file_not_for_the_client_is_nothing_to_them_unless_they_see_it_now_and_then_it_is_removed()
    {
        // the team's own file, never shown: no state at all
        PortalPublishRules.StateOf(Item(1, 1024, published: false, forClient: false)).ShouldBeNull();
        PortalPublishRules.IsClientRelevant(forClient: false, publishedFileId: null).ShouldBeFalse();

        // shown before and no longer chosen: clients lose it at the next publish, whatever else changed
        PortalPublishRules.StateOf(Item(1, 1024, forClient: false)).ShouldBe(PortalItemState.Removed);
        PortalPublishRules.StateOf(Item(1, 1024, publishedTitle: "Old", forClient: false)).ShouldBe(PortalItemState.Removed);
        PortalPublishRules.IsClientRelevant(forClient: false, publishedFileId: 10).ShouldBeTrue();
        PortalPublishRules.IsClientRelevant(forClient: true, publishedFileId: null).ShouldBeTrue();
    }

    [Fact]
    public void The_teams_own_files_change_nothing_for_clients()
    {
        var items = new[]
        {
            Item(1, 1024),
            Item(2, 512, published: false, forClient: false),       // the team's, placed first
            Item(3, 2048, publishedTitle: "Old", forClient: false),  // no longer chosen: removed for clients
        };

        var changes = PortalPublishRules.ChangesOf(items, [], [], null, null);

        changes.Added.ShouldBe(0);
        changes.Updated.ShouldBe(0);
        changes.Removed.ShouldBe(1);
        changes.Reordered.ShouldBeEmpty();
        PortalPublishRules.ChangesOf([Item(1, 1024), Item(2, 512, published: false, forClient: false)], [], [], null, null).Any.ShouldBeFalse();
    }

    [Fact]
    public void An_unchosen_file_doesnt_make_its_section_reordered()
    {
        var items = new[] { Item(1, 2048, publishedOrder: 1024, forClient: false), Item(2, 1024, publishedOrder: 2048) };

        PortalPublishRules.ReorderedSections(items).ShouldBeEmpty();
    }

    [Fact]
    public void A_new_place_alone_is_not_a_changed_item_but_a_reordered_section()
    {
        var items = new[] { Item(1, 2048, publishedOrder: 1024), Item(2, 1024, publishedOrder: 2048) };

        items.Select(PortalPublishRules.StateOf).ShouldAllBe(s => s == PortalItemState.Unchanged);
        PortalPublishRules.ReorderedSections(items).ShouldBe([P]);
    }

    [Fact]
    public void Different_numbers_in_the_same_order_are_not_a_new_order()
    {
        var items = new[] { Item(1, 1024, publishedOrder: 5), Item(2, 4096, publishedOrder: 7) };

        PortalPublishRules.ReorderedSections(items).ShouldBeEmpty();
    }

    [Fact]
    public void New_removed_and_moved_items_dont_make_a_section_reordered()
    {
        var items = new[]
        {
            Item(1, 1024),
            Item(2, 512, published: false),            // new, placed first
            Item(3, 3072, removed: true),              // removed
            Item(4, 256, section: M, publishedSection: P, publishedOrder: 2048), // moved away
        };

        PortalPublishRules.ReorderedSections(items).ShouldBeEmpty();
    }

    [Fact]
    public void The_changes_count_every_kind_and_the_hero_and_introduction()
    {
        var items = new[] { Item(1, 1024, published: false), Item(2, 2048, publishedTitle: "Old"), Item(3, 3072, removed: true), Item(4, 4096) };

        var changes = PortalPublishRules.ChangesOf(items, heroFileIds: [5], publishedHeroFileIds: [], summary: "Hello", publishedSummary: "Hello");

        changes.Added.ShouldBe(1);
        changes.Updated.ShouldBe(1);
        changes.Removed.ShouldBe(1);
        changes.Reordered.ShouldBeEmpty();
        changes.HeroChanged.ShouldBeTrue();
        changes.SummaryChanged.ShouldBeFalse();
        changes.Any.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_to_publish_when_the_draft_is_what_clients_see()
    {
        var changes = PortalPublishRules.ChangesOf([Item(1, 1024)], [5, 6], [5, 6], null, "");

        changes.Any.ShouldBeFalse();
    }

    [Fact]
    public void The_hero_changed_when_the_draft_images_or_their_order_differ_from_the_published_ones()
    {
        PortalPublishRules.ChangesOf([], [5, 6], [5, 6], null, null).HeroChanged.ShouldBeFalse();
        PortalPublishRules.ChangesOf([], [6, 5], [5, 6], null, null).HeroChanged.ShouldBeTrue();
        PortalPublishRules.ChangesOf([], [5], [5, 6], null, null).HeroChanged.ShouldBeTrue();
        PortalPublishRules.ChangesOf([], [5, 6, 7], [5, 6], null, null).HeroChanged.ShouldBeTrue();
        PortalPublishRules.ChangesOf([], [], [5], null, null).Any.ShouldBeTrue();
        PortalPublishRules.ChangesOf([], [], [], null, null).Any.ShouldBeFalse();
    }

    [Fact]
    public void There_are_at_most_twelve_hero_images()
    {
        PortalPublishRules.CheckCanAddHero(0).ShouldBeNull();
        PortalPublishRules.CheckCanAddHero(11).ShouldBeNull();

        var full = PortalPublishRules.CheckCanAddHero(12)!;
        full.Kind.ShouldBe(DenialKind.BusinessRule);
        full.Code.ShouldBe(ErrorCodes.PortalHeroLimit);
    }

    [Fact]
    public void A_new_order_must_list_the_sections_files_exactly_once()
    {
        long[] current = [1, 2, 3];

        PortalPublishRules.IsWholeOrder(current, [3, 1, 2]).ShouldBeTrue();
        PortalPublishRules.IsWholeOrder(current, [3, 1]).ShouldBeFalse();
        PortalPublishRules.IsWholeOrder(current, [3, 1, 2, 4]).ShouldBeFalse();
        PortalPublishRules.IsWholeOrder(current, [3, 1, 1]).ShouldBeFalse();
        PortalPublishRules.IsWholeOrder(current, [3, 1, 4]).ShouldBeFalse();
        PortalPublishRules.SortOrderAt(0).ShouldBe(1024);
        PortalPublishRules.SortOrderAt(2).ShouldBe(3072);
    }

    [Fact]
    public void Only_jpeg_png_and_webp_images_can_be_the_hero()
    {
        PortalPublishRules.CanBeHero("image/jpeg").ShouldBeTrue();
        PortalPublishRules.CanBeHero("image/png").ShouldBeTrue();
        PortalPublishRules.CanBeHero("image/webp").ShouldBeTrue();
        PortalPublishRules.CanBeHero("image/heic").ShouldBeFalse();
        PortalPublishRules.CanBeHero("image/gif").ShouldBeFalse();
        PortalPublishRules.CanBeHero("video/mp4").ShouldBeFalse();
    }

    [Fact]
    public void An_empty_introduction_is_none()
    {
        PortalPublishRules.CleanSummary("  ").ShouldBeNull();
        PortalPublishRules.CleanSummary(null).ShouldBeNull();
        PortalPublishRules.CleanSummary("  Welcome\r\nto your project ").ShouldBe("Welcome\nto your project");
    }
}
