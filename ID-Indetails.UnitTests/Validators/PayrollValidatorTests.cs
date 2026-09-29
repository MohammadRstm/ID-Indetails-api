using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.Validators.Payroll;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class PayrollValidatorTests
{
    private static readonly MyHoursQueryValidator HoursValidator = new();

    [Theory]
    [InlineData(1, true)]
    [InlineData(MyHoursQueryValidator.MaxPage, true)]
    [InlineData(0, false)]
    [InlineData(MyHoursQueryValidator.MaxPage + 1, false)]
    [InlineData(int.MaxValue, false)]
    public void The_hours_ledger_page_is_bounded(int page, bool valid)
        => HoursValidator.Validate(new MyHoursQuery { Page = page }).IsValid.ShouldBe(valid);
}
