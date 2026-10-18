using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class WebsiteRulesTests
{
    [Theory]
    [InlineData(ProjectStatus.Active, WebsiteProjectStatus.InProgress)]
    [InlineData(ProjectStatus.Hold, WebsiteProjectStatus.InProgress)]
    [InlineData(ProjectStatus.Completed, WebsiteProjectStatus.Completed)]
    [InlineData(ProjectStatus.Archived, WebsiteProjectStatus.Completed)]
    public void The_website_shows_only_in_progress_or_completed(ProjectStatus status, WebsiteProjectStatus shown)
        => WebsiteRules.StatusOf(status).ShouldBe(shown);

    [Fact]
    public void The_year_is_the_completion_else_the_target_else_the_start()
    {
        var start = new DateOnly(2021, 3, 1);
        WebsiteRules.YearOf(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2025, 1, 1), start).ShouldBe(2024);
        WebsiteRules.YearOf(null, new DateOnly(2025, 1, 1), start).ShouldBe(2025);
        WebsiteRules.YearOf(null, null, start).ShouldBe(2021);
    }

    [Theory]
    [InlineData("Villa Rizk", "villa-rizk")]
    [InlineData("  Hôtel   Le Gray — Beirut! ", "hotel-le-gray-beirut")]
    [InlineData("B&B 2024", "b-b-2024")]
    [InlineData("مشروع", "project")]
    public void A_slug_is_made_from_the_title(string title, string slug)
    {
        WebsiteRules.SlugOf(title).ShouldBe(slug);
        WebsiteRules.IsSlug(WebsiteRules.SlugOf(title)).ShouldBeTrue();
    }

    [Fact]
    public void A_long_title_makes_a_slug_that_still_fits()
    {
        var slug = WebsiteRules.SlugOf(string.Join(" ", Enumerable.Repeat("terrace", 40)));
        slug.Length.ShouldBeLessThanOrEqualTo(WebsiteRules.MaxSlugLength);
        slug.ShouldNotEndWith("-");
    }

    [Theory]
    [InlineData("villa-rizk", true)]
    [InlineData("v2", true)]
    [InlineData("Villa-Rizk", false)]
    [InlineData("villa--rizk", false)]
    [InlineData("-villa", false)]
    [InlineData("villa rizk", false)]
    public void Only_lowercase_words_joined_by_single_dashes_are_slugs(string value, bool valid)
        => WebsiteRules.IsSlug(value).ShouldBe(valid);

    [Fact]
    public void Publishing_needs_everything_the_website_shows()
    {
        WebsiteRules.MissingToPublish(WebsiteProjectStatus.Completed, 2022, "Beirut", "Lebanon", 33.9m, 35.5m, "A villa.", 1, 2).ShouldBeEmpty();
        WebsiteRules.MissingToPublish(null, null, " ", null, 33.9m, null, null, null, null).ShouldBe(
            ["a status", "a year", "a city", "a country", "a position on the map", "a description", "a thumbnail", "a main picture"]);
    }

    [Fact]
    public void A_new_order_lists_every_current_item_once()
    {
        WebsiteRules.IsWholeOrder([1, 2, 3], [3, 1, 2]).ShouldBeTrue();
        WebsiteRules.IsWholeOrder([1, 2, 3], [3, 1]).ShouldBeFalse();
        WebsiteRules.IsWholeOrder([1, 2, 3], [3, 3, 1]).ShouldBeFalse();
        WebsiteRules.IsWholeOrder([1, 2], [1, 2, 4]).ShouldBeFalse();
    }

    [Fact]
    public void The_public_site_reads_its_own_words()
    {
        WebsiteRules.PublicStatus(WebsiteProjectStatus.InProgress).ShouldBe("in-progress");
        WebsiteRules.PublicStatus(WebsiteProjectStatus.Completed).ShouldBe("completed");
        WebsiteRules.PublicType(WebsiteProjectType.Residential).ShouldBe("residential");
    }

    [Theory]
    [InlineData("+961 70 123 456", true)]
    [InlineData("(01) 234-567", true)]
    [InlineData("12345", false)]
    [InlineData("+961 70 abc", false)]
    [InlineData("961+70123456", false)]
    public void A_contact_phone_has_six_digits_and_only_phone_characters(string phone, bool valid)
        => WebsiteContactRules.IsPhone(phone).ShouldBe(valid);

    [Fact]
    public void Anything_in_the_hidden_field_is_a_robot()
    {
        WebsiteContactRules.IsRobot(null).ShouldBeFalse();
        WebsiteContactRules.IsRobot("").ShouldBeFalse();
        WebsiteContactRules.IsRobot("x").ShouldBeTrue();
    }
}
