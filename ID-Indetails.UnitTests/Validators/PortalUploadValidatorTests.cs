using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Portal;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class PortalUploadValidatorTests
{
    private static StartPortalUploadRequest Upload(long size) => new()
    {
        ClientToken = "4f9c2b7e-0d1a-4c3e-9b8f-2a6d5e7c1f03",
        Section = PortalSection.Presentation,
        FileName = "render.mp4",
        Size = size,
    };

    [Theory]
    [InlineData(1)]
    [InlineData(PortalContentRules.MaxFileBytes)]
    public void A_file_of_up_to_1_GB_can_be_sent(long size)
        => new StartPortalUploadRequestValidator().Validate(Upload(size)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(PortalContentRules.MaxFileBytes + 1)]
    public void An_empty_file_or_one_over_1_GB_is_refused(long size)
        => new StartPortalUploadRequestValidator().Validate(Upload(size))
            .Errors.Select(e => e.PropertyName).ShouldBe(["Size"]);
}
