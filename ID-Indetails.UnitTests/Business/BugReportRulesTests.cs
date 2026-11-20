using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.Validators.Support;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class BugReportRulesTests
{
    private static bool Valid(ReportBugRequest request) => new ReportBugRequestValidator().Validate(request).IsValid;

    private static ReportBugRequest Message(string? page) => new() { Description = "I tried to save and it failed", Page = page };

    [Theory]
    [InlineData("/design/projects/12")]
    [InlineData("/company/overhead")]
    [InlineData(null)]
    public void The_page_is_an_app_path(string? page) => Valid(Message(page)).ShouldBeTrue();

    [Theory]
    [InlineData("/reset-password?token=abc")]
    [InlineData("/accept-invitation#token")]
    [InlineData("https://elsewhere.example/x")]
    [InlineData("design/projects")]
    [InlineData("/projects/<b>")]
    public void Anything_but_a_path_is_refused_so_no_token_or_markup_travels(string page) => Valid(Message(page)).ShouldBeFalse();

    [Fact]
    public void What_happened_is_needed_and_anything_else_is_optional()
    {
        Valid(new ReportBugRequest { Description = " " }).ShouldBeFalse();
        Valid(new ReportBugRequest { Description = "It broke", Details = null }).ShouldBeTrue();
        Valid(new ReportBugRequest { Description = "It broke", Details = new string('x', 4001) }).ShouldBeFalse();
    }
}
