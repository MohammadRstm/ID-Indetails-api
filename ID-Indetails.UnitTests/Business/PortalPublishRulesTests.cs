using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PortalPublishRulesTests
{
    private const PortalSection P = PortalSection.Presentation;
    private const PortalSection M = PortalSection.Model;

    /// <summary>An item published as it is now, unless told otherwise.</summary>
    private static PortalItemCopies Item(
        long id, int order, PortalSection section = P, long? file = null, string title = "Title",
        bool published = true, long? publishedFile = null, string? publishedTitle = null, PortalSection? publishedSection = null,
        int? publishedOrder = null, bool removed = false)
    {
        var fileId = file ?? id * 10;
        return published
            ? new(id, fileId, title, section, order, publishedFile ?? fileId, publishedTitle ?? title, publishedSection ?? section, publishedOrder ?? order, removed)
            : new(id, fileId, title, section, order, null, null, null, null, false);
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

        var changes = PortalPublishRules.ChangesOf(items, heroFileId: 5, publishedHeroFileId: null, summary: "Hello", publishedSummary: "Hello");

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
        var changes = PortalPublishRules.ChangesOf([Item(1, 1024)], 5, 5, null, "");

        changes.Any.ShouldBeFalse();
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
