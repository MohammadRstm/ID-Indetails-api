using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.Validators.Materials;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class MaterialValidatorTests
{
    private static NewMaterialRequest Item(Guid? clientRef) => new() { Name = "Cement", Amount = 120m, ClientRef = clientRef };

    [Fact]
    public void Client_refs_are_optional_and_used_once_in_a_batch()
    {
        var validator = new AddMaterialsRequestValidator();
        var once = Guid.NewGuid();

        validator.Validate(new AddMaterialsRequest { Items = [Item(once), Item(Guid.NewGuid()), Item(null), Item(null)] }).IsValid.ShouldBeTrue();
        validator.Validate(new AddMaterialsRequest { Items = [Item(once), Item(once)] })
            .Errors.Select(e => e.PropertyName).ShouldBe(["Items"]);
        validator.Validate(new AddMaterialsRequest { Items = [Item(Guid.Empty)] }).IsValid.ShouldBeFalse();
    }
}
