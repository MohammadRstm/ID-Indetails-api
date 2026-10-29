using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.Validators.SiteExpenses;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class SiteExpenseValidatorTests
{
    private static NewSiteExpenseRequest Item(Guid? clientRef) => new() { Name = "Cement", Amount = 120m, ClientRef = clientRef };

    [Fact]
    public void Client_refs_are_optional_and_used_once_in_a_batch()
    {
        var validator = new AddSiteExpensesRequestValidator();
        var once = Guid.NewGuid();

        validator.Validate(new AddSiteExpensesRequest { Items = [Item(once), Item(Guid.NewGuid()), Item(null), Item(null)] }).IsValid.ShouldBeTrue();
        validator.Validate(new AddSiteExpensesRequest { Items = [Item(once), Item(once)] })
            .Errors.Select(e => e.PropertyName).ShouldBe(["Items"]);
        validator.Validate(new AddSiteExpensesRequest { Items = [Item(Guid.Empty)] }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_batch_is_one_to_a_hundred_expenses()
    {
        var validator = new AddSiteExpensesRequestValidator();

        validator.Validate(new AddSiteExpensesRequest { Items = [] }).IsValid.ShouldBeFalse();
        validator.Validate(new AddSiteExpensesRequest { Items = Enumerable.Range(0, 100).Select(_ => Item(null)).ToList() }).IsValid.ShouldBeTrue();
        validator.Validate(new AddSiteExpensesRequest { Items = Enumerable.Range(0, 101).Select(_ => Item(null)).ToList() }).IsValid.ShouldBeFalse();
    }
}
