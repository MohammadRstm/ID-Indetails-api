using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.Validators.Website;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class WebsiteAnalyticsValidatorTests
{
    private static IEnumerable<string> Errors(DateOnly? from, DateOnly? to)
        => new WebsiteAnalyticsQueryValidator().Validate(new WebsiteAnalyticsQuery { From = from, To = to }).Errors.Select(e => e.PropertyName);

    [Fact]
    public void Both_days_or_neither()
    {
        Errors(null, null).ShouldBeEmpty();
        Errors(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)).ShouldBeEmpty();
        Errors(new DateOnly(2026, 10, 1), null).ShouldBe(["To"]);
        Errors(null, new DateOnly(2026, 10, 1)).ShouldBe(["From"]);
    }

    [Fact]
    public void A_period_ends_after_it_starts_and_covers_at_most_366_days()
    {
        Errors(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)).ShouldBe(["To"]);
        Errors(new DateOnly(2025, 10, 20), new DateOnly(2026, 10, 20)).ShouldBeEmpty();
        Errors(new DateOnly(2025, 10, 19), new DateOnly(2026, 10, 20)).ShouldBe(["To"]);
    }

    [Fact]
    public void Google_Analytics_has_nothing_before_August_2015()
        => Errors(new DateOnly(2015, 8, 13), new DateOnly(2015, 9, 1)).ShouldBe(["From"]);
}
