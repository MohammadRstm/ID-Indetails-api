using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.Validators.Boqs;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class BoqProgressValidatorTests
{
    private static readonly RecordBoqProgressRequestValidator Validator = new();

    private static RecordBoqProgressRequest Request(params BoqProgressLineRequest[] lines) => new() { Lines = lines };

    private static IReadOnlyList<string> Errors(RecordBoqProgressRequest request)
        => Validator.Validate(request).Errors.Select(e => e.PropertyName).Distinct().ToList();

    [Fact]
    public void An_executed_quantity_is_zero_or_more_to_four_places()
    {
        Validator.Validate(Request(new BoqProgressLineRequest(4, 0m), new BoqProgressLineRequest(5, 12.3456m, "Ground floor done"))).IsValid.ShouldBeTrue();
        Validator.Validate(Request(new BoqProgressLineRequest(4, RecordBoqProgressRequestValidator.MaxQuantity))).IsValid.ShouldBeTrue();

        Errors(Request(new BoqProgressLineRequest(4, -0.0001m))).ShouldBe(["Lines[0].ExecutedQuantity"]);
        Errors(Request(new BoqProgressLineRequest(4, 1.23456m))).ShouldBe(["Lines[0].ExecutedQuantity"]);
        Errors(Request(new BoqProgressLineRequest(4, 10_000_000_000m))).ShouldBe(["Lines[0].ExecutedQuantity"]);
    }

    [Fact]
    public void One_to_two_hundred_lines_each_valid_and_recorded_once_with_a_short_note()
    {
        Errors(Request()).ShouldBe(["Lines"]);
        Errors(Request(Enumerable.Range(1, BoqProgressRules.MaxLines + 1).Select(i => new BoqProgressLineRequest(i, 1m)).ToArray())).ShouldBe(["Lines"]);
        Validator.Validate(Request(Enumerable.Range(1, BoqProgressRules.MaxLines).Select(i => new BoqProgressLineRequest(i, 1m)).ToArray()))
            .IsValid.ShouldBeTrue();

        Errors(Request(new BoqProgressLineRequest(4, 1m), new BoqProgressLineRequest(4, 2m))).ShouldBe(["Lines"]);
        Errors(Request(new BoqProgressLineRequest(0, 1m))).ShouldBe(["Lines[0].BoqLineId"]);
        Errors(Request(new BoqProgressLineRequest(4, 1m, new string('a', BoqProgressRules.MaxNoteLength + 1)))).ShouldBe(["Lines[0].Note"]);
        Validator.Validate(Request(new BoqProgressLineRequest(4, 1m, new string('a', BoqProgressRules.MaxNoteLength)))).IsValid.ShouldBeTrue();
    }
}
