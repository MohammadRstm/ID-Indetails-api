using System.Globalization;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.Validators.Payroll;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class PayrollValidatorTests
{
    private static readonly MyHoursQueryValidator HoursValidator = new();
    private static readonly MyScheduleQueryValidator ScheduleValidator = new();

    [Theory]
    [InlineData(1, true)]
    [InlineData(MyHoursQueryValidator.MaxPage, true)]
    [InlineData(0, false)]
    [InlineData(MyHoursQueryValidator.MaxPage + 1, false)]
    [InlineData(int.MaxValue, false)]
    public void The_hours_ledger_page_is_bounded(int page, bool valid)
        => HoursValidator.Validate(new MyHoursQuery { Page = page }).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData(null, true)]
    [InlineData("2000-01-01", true)]
    [InlineData("2100-12-31", true)]
    [InlineData("1999-12-31", false)]
    [InlineData("2101-01-01", false)]
    public void The_schedule_date_is_between_2000_and_2100(string? date, bool valid)
        => ScheduleValidator.Validate(new MyScheduleQuery { Date = date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture) }).IsValid.ShouldBe(valid);
}
