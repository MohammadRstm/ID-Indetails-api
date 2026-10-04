using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.Validators.Support;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class BugReportRulesTests
{
    private static bool Valid(ReportBugRequest request) => new ReportBugRequestValidator().Validate(request).IsValid;

    private static ReportBugRequest Report(string? page) => new() { Title = "It broke", Description = "It did", Page = page };

    [Theory]
    [InlineData("/design/projects/12")]
    [InlineData("/company/overhead")]
    [InlineData(null)]
    public void The_page_is_an_app_path(string? page) => Valid(Report(page)).ShouldBeTrue();

    [Theory]
    [InlineData("/reset-password?token=abc")]
    [InlineData("/accept-invitation#token")]
    [InlineData("https://elsewhere.example/x")]
    [InlineData("design/projects")]
    [InlineData("/projects/<b>")]
    public void Anything_but_a_path_is_refused_so_no_token_or_markup_travels(string page) => Valid(Report(page)).ShouldBeFalse();

    [Fact]
    public void A_title_and_a_description_are_needed_and_the_title_is_one_line()
    {
        Valid(new ReportBugRequest { Title = " ", Description = "It did" }).ShouldBeFalse();
        Valid(new ReportBugRequest { Title = "It broke", Description = "" }).ShouldBeFalse();
        Valid(new ReportBugRequest { Title = "It\r\nbroke", Description = "It did" }).ShouldBeFalse();
    }
}
