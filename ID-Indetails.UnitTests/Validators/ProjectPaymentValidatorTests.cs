using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Projects;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class ProjectPaymentValidatorTests
{
    private static readonly string TooLong = new('x', ProjectRules.MaxPaymentDescriptionLength + 1);

    [Fact]
    public void A_design_payment_needs_an_amount_and_why()
    {
        var validator = new UpdateProjectPaymentRequestValidator();

        validator.Validate(new UpdateProjectPaymentRequest { Revenue = 6500m, Description = "The client added a guest house" }).IsValid.ShouldBeTrue();
        validator.Validate(new UpdateProjectPaymentRequest { Revenue = 0m, Description = "Free of charge" }).IsValid.ShouldBeTrue();
        validator.Validate(new UpdateProjectPaymentRequest { Revenue = 6500m, Description = "  " })
            .Errors.Select(e => e.PropertyName).ShouldBe(["Description"]);
        validator.Validate(new UpdateProjectPaymentRequest { Revenue = 6500m, Description = TooLong })
            .Errors.Select(e => e.PropertyName).ShouldBe(["Description"]);
        validator.Validate(new UpdateProjectPaymentRequest { Revenue = -1m, Description = "Less" })
            .Errors.Select(e => e.PropertyName).ShouldBe(["Revenue"]);
        validator.Validate(new UpdateProjectPaymentRequest { Revenue = 10.555m, Description = "Odd cents" }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void An_execution_price_change_needs_why_too()
    {
        var validator = new UpdateProjectPricingRequestValidator();

        validator.Validate(new UpdateProjectPricingRequest { MaterialMarkup = 0.15m, Description = "New supplier terms" }).IsValid.ShouldBeTrue();
        validator.Validate(new UpdateProjectPricingRequest { MaterialMarkup = 0.15m })
            .Errors.Select(e => e.PropertyName).ShouldBe(["Description"]);
        validator.Validate(new UpdateProjectPricingRequest { MaterialMarkup = 0.15m, Description = TooLong })
            .Errors.Select(e => e.PropertyName).ShouldBe(["Description"]);
        validator.Validate(new UpdateProjectPricingRequest { MaterialMarkup = 6m, Description = "Too much" }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void The_work_price_is_never_typed_in()
    {
        new UpdateProjectPricingRequestValidator()
            .Validate(new UpdateProjectPricingRequest { ProjectFee = 1500m, MaterialMarkup = 0.15m, Description = "An extra floor" })
            .Errors.Select(e => e.PropertyName).ShouldBe(["ProjectFee"]);
        new CreateProjectRequestValidator()
            .Validate(new CreateProjectRequest { Title = "Site", ProjectFee = 1500m })
            .Errors.Select(e => e.PropertyName).ShouldContain("ProjectFee");
    }

    [Fact]
    public void Supervision_is_a_fraction_between_0_and_1_with_at_most_4_decimals()
    {
        var validator = new UpdateProjectPricingRequestValidator();
        bool Valid(decimal? rate) => validator.Validate(new UpdateProjectPricingRequest { SupervisionRate = rate, Description = "Why" }).IsValid;

        Valid(null).ShouldBeTrue();
        Valid(0m).ShouldBeTrue();
        Valid(0.08m).ShouldBeTrue();
        Valid(1m).ShouldBeTrue();
        Valid(-0.01m).ShouldBeFalse();
        Valid(1.01m).ShouldBeFalse();
        Valid(0.12345m).ShouldBeFalse();

        new CreateProjectRequestValidator().Validate(new CreateProjectRequest { Title = "Site", SupervisionRate = 2m })
            .Errors.Select(e => e.PropertyName).ShouldContain("SupervisionRate");
    }

    [Fact]
    public void Client_and_type_carry_no_payment()
    {
        var validator = new UpdateProjectBillingRequestValidator();

        validator.Validate(new UpdateProjectBillingRequest { BillingType = BillingType.Internal }).IsValid.ShouldBeTrue();
        validator.Validate(new UpdateProjectBillingRequest { BillingType = BillingType.Client, ClientId = 0 }).IsValid.ShouldBeFalse();
    }
}
