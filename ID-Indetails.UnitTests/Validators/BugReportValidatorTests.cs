using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.Validators.Support;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

/// <summary>A bug report can carry the references of the requests that failed lately, and nothing else with them.</summary>
public class BugReportValidatorTests
{
    private static ReportBugRequest Report(params ReportedErrorRequest[] errors)
        => new() { Title = "It broke", Description = "The page went blank.", RecentErrors = errors };

    private static ReportedErrorRequest Error(string reference = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", int status = 500, string? path = "/api/finance/design/overview")
        => new() { Ref = reference, Status = status, Path = path, At = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void Recent_errors_with_their_trace_ids_are_accepted()
    {
        new ReportBugRequestValidator().Validate(Report(Error(), Error("0HN7Q2:00000001", 404, null))).IsValid.ShouldBeTrue();
        new ReportBugRequestValidator().Validate(Report()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("<script>", 500, "/api/x")]
    [InlineData("", 500, "/api/x")]
    [InlineData("00-abc", 200, "/api/x")]
    [InlineData("00-abc", 500, "/api/reset-password?token=secret")]
    public void A_reference_status_or_path_out_of_shape_is_refused(string reference, int status, string path)
        => new ReportBugRequestValidator().Validate(Report(Error(reference, status, path))).IsValid.ShouldBeFalse();

    [Fact]
    public void At_most_five_recent_errors()
        => new ReportBugRequestValidator().Validate(Report(Error(), Error(), Error(), Error(), Error(), Error())).IsValid.ShouldBeFalse();
}
