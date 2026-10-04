using ID_Indetails.LoadTests.Tool.Safety;
using Shouldly;

namespace ID_Indetails.LoadTests.Tool.Tests;

public class EmailSafetyTests
{
    [Theory]
    [InlineData("lt.design.employee.001@loadtest.invalid")]
    [InlineData("LT.Admin@LOADTEST.INVALID")]
    [InlineData("a@loadtest.invalid, b@loadtest.invalid")]
    [InlineData("Billing <billing@loadtest.invalid>")]
    [InlineData("+961 70 123 456")]
    [InlineData("")]
    [InlineData(null)]
    public void Undeliverable_or_no_address(string? text)
    {
        EmailSafety.HasDeliverableAddress(text).ShouldBeFalse();
    }

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("a@loadtest.invalid, b@company.com")]
    [InlineData("x@loadtest.invalid.com")]
    [InlineData("x@loadtest.invalidated.net")]
    [InlineData("x@sub.loadtest.invalid")]
    [InlineData("x@loadtest.invalid@gmail.com")]
    public void Deliverable_address(string text)
    {
        EmailSafety.HasDeliverableAddress(text).ShouldBeTrue();
    }
}
