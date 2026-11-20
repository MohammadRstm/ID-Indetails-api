using ID_Indetails.api.Business.Projects;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectColorRulesTests
{
    private static readonly string[] Hexes = ProjectColorRules.Palette.Select(c => c.Hex).ToArray();

    [Fact]
    public void The_palette_has_thirty_different_colours_with_different_names()
    {
        Hexes.Length.ShouldBe(30);
        Hexes.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(30);
        ProjectColorRules.Palette.Select(c => c.Name).Distinct().Count().ShouldBe(30);
        Hexes.ShouldAllBe(h => ProjectColorRules.IsColor(h));
    }

    [Fact]
    public void A_new_project_gets_the_colour_no_open_project_has()
    {
        // whatever case the open projects' colours were saved in
        var taken = Hexes.Where(h => h != "#2A7F62").Select(h => h.ToLowerInvariant()).ToList();

        for (var seed = 0; seed < 20; seed++)
        {
            ProjectColorRules.Pick(taken, new Random(seed)).ShouldBe("#2A7F62");
        }
    }

    [Fact]
    public void With_every_colour_taken_it_picks_one_of_the_least_used()
    {
        var taken = Hexes.Concat(Hexes.Where(h => h != "#1D2D44")).Append("#123456").ToList();

        ProjectColorRules.Pick(taken, new Random(1)).ShouldBe("#1D2D44");
    }

    [Fact]
    public void With_no_open_project_any_palette_colour_may_come()
    {
        var picked = Enumerable.Range(0, 200).Select(seed => ProjectColorRules.Pick([], new Random(seed))).ToHashSet();

        picked.ShouldBeSubsetOf(Hexes);
        picked.Count.ShouldBeGreaterThan(15);
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

        ProjectColorRules.HexOf(" Jade ").ShouldBe("#2A7F62");
        ProjectColorRules.HexOf("chartreuse").ShouldBeNull();
        ProjectColorRules.NameOf("#2a7f62").ShouldBe("jade");
        ProjectColorRules.NameOf("#123456").ShouldBeNull();
    }
}
