using System.IO.Compression;
using System.Text;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Services.Geocoding.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class WebsitePlacesTests
{
    /// <summary>A few places in the bundled list's format: name, country code, latitude, longitude, population.</summary>
    private static GeoNamesDirectory Directory(params string[] lines) => new(() =>
    {
        var zipped = new MemoryStream();
        using (var gzip = new GZipStream(zipped, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        }

        zipped.Position = 0;
        return zipped;
    });

    private static readonly GeoNamesDirectory Lebanon = Directory(
        "Beirut\tLB\t33.88894\t35.49442\t1916100",
        "Jbeil\tLB\t34.12111\t35.64806\t40000",
        "Batroun\tLB\t34.25528\t35.65806\t9000",
        "Saïda\tLB\t33.56306\t35.36889\t163554",
        "Batroun\tLB\t34.20000\t35.70000\t1200",
        "Paris\tFR\t48.85341\t2.3488\t2138551",
        "not a line",
        "Nowhere\tLB\tx\t35\t1");

    [Fact]
    public void Countries_are_the_ones_with_towns_by_their_English_names()
    {
        Lebanon.Countries.Select(c => (c.Code, c.Name)).ShouldBe([("FR", "France"), ("LB", "Lebanon")]);
        Lebanon.Country("lebanon")!.Code.ShouldBe("LB");
        Lebanon.Country("lb")!.Name.ShouldBe("Lebanon");
        Lebanon.Country("Atlantis").ShouldBeNull();
    }

    [Fact]
    public void Cities_come_biggest_first_and_typing_finds_them_whatever_the_accents()
    {
        Lebanon.Cities("LB", null, 3).Select(c => c.Name).ShouldBe(["Beirut", "Saïda", "Jbeil"]);
        // those starting with it first, then those containing it
        Lebanon.Cities("LB", "sai", 10).Select(c => c.Name).ShouldBe(["Saïda"]);
        Lebanon.Cities("LB", "ou", 10).Select(c => c.Name).ShouldBe(["Batroun", "Batroun"]);
        Lebanon.Cities("LB", "ei", 10).Select(c => c.Name).ShouldBe(["Beirut", "Jbeil"]);
        Lebanon.Cities("FR", "beirut", 10).ShouldBeEmpty();
    }

    [Fact]
    public void A_name_finds_its_city_the_biggest_when_several_share_it()
    {
        Lebanon.City("LB", "  SAIDA ")!.Name.ShouldBe("Saïda");
        Lebanon.City("LB", "Batroun")!.Population.ShouldBe(9000);
        Lebanon.City("FR", "Beirut").ShouldBeNull();
    }

    [Fact]
    public void The_nearest_town_is_found_within_the_distance_asked()
    {
        Lebanon.Nearest(34.12m, 35.65m, 30)!.Name.ShouldBe("Jbeil");
        // the middle of the sea: nothing within 30 km
        Lebanon.Nearest(34.0m, 34.5m, 30).ShouldBeNull();
    }

    [Fact]
    public void A_random_town_is_one_of_the_country_s()
    {
        var random = new Random(3);
        for (var i = 0; i < 20; i++)
        {
            Lebanon.RandomCity("LB", random)!.CountryCode.ShouldBe("LB");
        }

        Lebanon.RandomCity("DE", random).ShouldBeNull();
    }

    [Fact]
    public void The_bundled_list_has_the_world_s_countries_and_Lebanon_s_towns()
    {
        var world = new GeoNamesDirectory();
        world.Countries.Count.ShouldBeGreaterThan(200);
        world.Country("Lebanon")!.Code.ShouldBe("LB");
        world.Cities("LB", null, 1).Single().Name.ShouldBe("Beirut");
        world.City("LB", "batroun")!.Name.ShouldBe("Batroûn");
        world.Nearest(34.1212m, 35.6481m, 10)!.Name.ShouldBe("Byblos");
    }

    [Fact]
    public void Without_the_list_the_lists_are_empty_and_nothing_breaks()
    {
        var none = new GeoNamesDirectory(() => null);
        none.Countries.ShouldBeEmpty();
        none.Nearest(33.9m, 35.5m, 100).ShouldBeNull();
    }

    [Fact]
    public void A_picked_point_stays_within_a_kilometre_of_its_town()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var (lat, lng) = WebsitePlaces.RandomPointNear(33.88894m, 35.49442m, random);
            Math.Abs(lat - 33.88894m).ShouldBeLessThanOrEqualTo(WebsitePlaces.Spread);
            Math.Abs(lng - 35.49442m).ShouldBeLessThanOrEqualTo(WebsitePlaces.Spread);
            (Math.Round(lat, WebsitePlaces.Decimals) == lat).ShouldBeTrue();
        }
    }

    [Fact]
    public void Places_compare_whatever_the_case_and_spaces()
    {
        WebsitePlaces.SamePlace(" beirut", "Beirut ").ShouldBeTrue();
        WebsitePlaces.SamePlace("Beirut", "Jbeil").ShouldBeFalse();
        WebsitePlaces.SamePlace(null, null).ShouldBeTrue();
    }
}
