using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.Validators.WorkEntries;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class WorkEntryValidatorTests
{
    private static readonly ProjectWorkQueryValidator ProjectWorkValidator = new();

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void The_project_work_text_filter_is_at_most_100_characters(int length, bool valid)
        => ProjectWorkValidator.Validate(new ProjectWorkQuery { Search = new string('a', length) }).IsValid.ShouldBe(valid);

    [Fact]
    public void The_project_work_range_ends_on_or_after_its_start()
    {
        ProjectWorkValidator.Validate(new ProjectWorkQuery { From = new DateOnly(2026, 6, 20), To = new DateOnly(2026, 6, 15) }).IsValid.ShouldBeFalse();
        ProjectWorkValidator.Validate(new ProjectWorkQuery { From = new DateOnly(2026, 6, 15), To = new DateOnly(2026, 6, 15) }).IsValid.ShouldBeTrue();
    }
}
