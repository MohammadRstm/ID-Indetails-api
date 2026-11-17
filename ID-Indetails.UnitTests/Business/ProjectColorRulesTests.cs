using ID_Indetails.api.Business.Projects;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectColorRulesTests
{
    private static readonly string[] Hexes = ProjectColorRules.Palette.Select(c => c.Hex).ToArray();

    [Fact]
    public void The_palette_has_sixteen_different_colours_with_different_names()
    {
        Hexes.Length.ShouldBe(16);
        Hexes.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(16);
        ProjectColorRules.Palette.Select(c => c.Name).Distinct().Count().ShouldBe(16);
        Hexes.ShouldAllBe(h => ProjectColorRules.IsColor(h));
    }

    [Fact]
    public void A_new_project_gets_the_colour_no_open_project_has()
    {
        // whatever case the open projects' colours were saved in
        var taken = Hexes.Where(h => h != "#0F9F75").Select(h => h.ToLowerInvariant()).ToList();

        for (var seed = 0; seed < 20; seed++)
        {
            ProjectColorRules.Pick(taken, new Random(seed)).ShouldBe("#0F9F75");
        }
    }

    [Fact]
    public void With_every_colour_taken_it_picks_one_of_the_least_used()
    {
        var taken = Hexes.Concat(Hexes.Where(h => h != "#2F6FEB")).Append("#123456").ToList();

        ProjectColorRules.Pick(taken, new Random(1)).ShouldBe("#2F6FEB");
    }

    [Fact]
    public void With_no_open_project_any_palette_colour_may_come()
    {
        var picked = Enumerable.Range(0, 200).Select(seed => ProjectColorRules.Pick([], new Random(seed))).ToHashSet();

        picked.ShouldBeSubsetOf(Hexes);
        picked.Count.ShouldBeGreaterThan(8);
    }

    [Fact]
    public void A_colour_is_six_or_eight_hex_digits_and_palette_colours_go_by_their_names()
    {
        ProjectColorRules.IsColor("#1f6feb").ShouldBeTrue();
        ProjectColorRules.IsColor("#1F6FEB80").ShouldBeTrue();
        ProjectColorRules.IsColor("1F6FEB").ShouldBeFalse();
        ProjectColorRules.IsColor("#1F6").ShouldBeFalse();
        ProjectColorRules.IsColor("green").ShouldBeFalse();
        ProjectColorRules.IsColor(null).ShouldBeFalse();

        ProjectColorRules.HexOf(" Green ").ShouldBe("#2F9E44");
        ProjectColorRules.HexOf("chartreuse").ShouldBeNull();
        ProjectColorRules.NameOf("#2f9e44").ShouldBe("green");
        ProjectColorRules.NameOf("#123456").ShouldBeNull();
    }
}
