using ID_Indetails.api.Business.Website;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class WebsitePlacesTests
{
    [Fact]
    public void The_city_is_the_most_specific_name_the_address_has()
    {
        WebsitePlaces.PlaceOf(new Dictionary<string, string> { ["city"] = "Beirut", ["state"] = "Beirut Governorate", ["country"] = "Lebanon" })
            .ShouldBe(new GeoPlace("Beirut", "Lebanon"));
        WebsitePlaces.PlaceOf(new Dictionary<string, string> { ["town"] = "Batroun", ["county"] = "Batroun District", ["country"] = "Lebanon" })
            .ShouldBe(new GeoPlace("Batroun", "Lebanon"));
        WebsitePlaces.PlaceOf(new Dictionary<string, string> { ["village"] = "Ehden", ["country"] = "Lebanon" }).City.ShouldBe("Ehden");
        // only a region: better than nothing
        WebsitePlaces.PlaceOf(new Dictionary<string, string> { ["state"] = "Mount Lebanon", ["country"] = "Lebanon" }).City.ShouldBe("Mount Lebanon");
        // the sea: no city, no country
        WebsitePlaces.PlaceOf(new Dictionary<string, string>()).ShouldBe(new GeoPlace(null, null));
    }

    [Fact]
    public void A_random_point_stays_in_the_inner_part_of_the_city_and_near_its_centre()
    {
        var city = new GeoArea(33.8938m, 35.5018m, 33.8600m, 33.9200m, 35.4600m, 35.5500m);
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var (lat, lng) = WebsitePlaces.RandomPointIn(city, random);
            // a quarter of the rectangle each way (its inner half), here less than the 0.02° cap
            Math.Abs(lat - city.Latitude).ShouldBeLessThanOrEqualTo(0.015m);
            Math.Abs(lng - city.Longitude).ShouldBeLessThanOrEqualTo(0.0225m);
            (Math.Round(lat, WebsitePlaces.Decimals) == lat).ShouldBeTrue();
        }
    }

    [Fact]
    public void A_wide_area_still_keeps_the_point_within_two_kilometres_of_the_centre()
    {
        var region = new GeoArea(34.0m, 35.8m, 33.0m, 35.0m, 35.0m, 36.6m);
        var random = new Random(11);
        for (var i = 0; i < 500; i++)
        {
            var (lat, lng) = WebsitePlaces.RandomPointIn(region, random);
            Math.Abs(lat - region.Latitude).ShouldBeLessThanOrEqualTo(WebsitePlaces.MaxSpread);
            Math.Abs(lng - region.Longitude).ShouldBeLessThanOrEqualTo(WebsitePlaces.MaxSpread);
        }
    }

    [Fact]
    public void Places_compare_whatever_the_case_and_spaces()
    {
        WebsitePlaces.SamePlace(" beirut", "Beirut ").ShouldBeTrue();
        WebsitePlaces.SamePlace("Beirut", "Byblos").ShouldBeFalse();
    }
}
