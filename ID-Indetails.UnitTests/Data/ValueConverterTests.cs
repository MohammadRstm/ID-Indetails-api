using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Data;

public class ValueConverterTests
{
    [Theory]
    [InlineData(ProjectStatus.Active, "active")]
    [InlineData(ProjectStatus.Hold, "hold")]
    [InlineData(EditWindow.OneDay, "one_day")]
    [InlineData(EditWindow.SevenDays, "seven_days")]
    [InlineData(ConversationType.Dm, "dm")]
    public void Enums_are_stored_as_snake_case(object value, string expected)
    {
        var method = typeof(SnakeCaseEnum).GetMethod(nameof(SnakeCaseEnum.ToDb))!
            .MakeGenericMethod(value.GetType());

        method.Invoke(null, [value]).ShouldBe(expected);
    }

    [Fact]
    public void Every_stored_value_reads_back_to_the_same_enum_member()
    {
        AssertRoundTrip<UserStatus>();
        AssertRoundTrip<ProjectStatus>();
        AssertRoundTrip<BillingType>();
        AssertRoundTrip<PayBasis>();
        AssertRoundTrip<ContactKind>();
        AssertRoundTrip<DeliveryChannel>();
        AssertRoundTrip<PaymentMethod>();
        AssertRoundTrip<OverheadCostGroup>();
        AssertRoundTrip<ConversationType>();
        AssertRoundTrip<EditWindow>();
        AssertRoundTrip<EntryNotificationMode>();
    }

    [Fact]
    public void Unknown_stored_value_fails_loudly()
    {
        Should.Throw<InvalidOperationException>(() => SnakeCaseEnum.FromDb<ProjectStatus>("paused"));
    }

    [Fact]
    public void Check_sql_lists_every_value()
    {
        SnakeCaseEnum.CheckSql<ProjectStatus>("status")
            .ShouldBe("`status` IN ('active', 'hold', 'completed', 'archived')");
    }

    [Fact]
    public void Datetimes_read_from_the_database_are_marked_utc()
    {
        var converter = new UtcDateTimeConverter();
        var stored = new DateTime(2026, 9, 25, 8, 30, 0, DateTimeKind.Unspecified);

        var read = (DateTime)converter.ConvertFromProvider(stored)!;

        read.Kind.ShouldBe(DateTimeKind.Utc);
        read.ShouldBe(stored);
    }

    [Fact]
    public void Local_datetimes_are_converted_to_utc_before_saving()
    {
        var converter = new UtcDateTimeConverter();
        var local = new DateTime(2026, 9, 25, 11, 30, 0, DateTimeKind.Local);

        var saved = (DateTime)converter.ConvertToProvider(local)!;

        saved.ShouldBe(local.ToUniversalTime());
        saved.Kind.ShouldBe(DateTimeKind.Utc);
    }

    private static void AssertRoundTrip<TEnum>() where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            SnakeCaseEnum.FromDb<TEnum>(SnakeCaseEnum.ToDb(value)).ShouldBe(value);
        }
    }
}
