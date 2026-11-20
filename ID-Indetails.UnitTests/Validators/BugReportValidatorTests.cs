using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.Validators.Support;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

/// <summary>
/// A message to the developer can carry the error it is about and the references of the requests that failed lately,
/// and nothing else with them.
/// </summary>
public class BugReportValidatorTests
{
    private const string TraceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static bool Valid(ReportBugRequest request) => new ReportBugRequestValidator().Validate(request).IsValid;

    private static ReportBugRequest Message(params ReportedErrorRequest[] errors)
        => new() { Description = "The page went blank.", RecentErrors = errors };

    private static ReportedErrorRequest Error(string reference = TraceId, int status = 500, string? path = "/api/finance/design/overview")
        => new() { Ref = reference, Status = status, Path = path, At = At };

    [Fact]
    public void Recent_errors_with_their_trace_ids_are_accepted()
    {
        Valid(Message(Error(), Error("0HN7Q2:00000001", 404, null))).ShouldBeTrue();
        Valid(Message()).ShouldBeTrue();
    }

    [Theory]
    [InlineData("<script>", 500, "/api/x")]
    [InlineData("", 500, "/api/x")]
    [InlineData("00-abc", 200, "/api/x")]
    [InlineData("00-abc", 500, "/api/reset-password?token=secret")]
    public void A_reference_status_or_path_out_of_shape_is_refused(string reference, int status, string path)
        => Valid(Message(Error(reference, status, path))).ShouldBeFalse();

    [Fact]
    public void At_most_five_recent_errors()
        => Valid(Message(Error(), Error(), Error(), Error(), Error(), Error())).ShouldBeFalse();

    [Fact]
    public void The_error_it_is_about_has_its_reference_or_the_browsers_message_and_when()
    {
        Valid(Message() with { Error = new ContactErrorRequest { Ref = TraceId, At = At } }).ShouldBeTrue();
        Valid(Message() with { Error = new ContactErrorRequest { Ref = "4bf92f35", At = At } }).ShouldBeTrue();
        Valid(Message() with { Error = new ContactErrorRequest { Message = "Cannot read properties of undefined", At = At } }).ShouldBeTrue();

        Valid(Message() with { Error = new ContactErrorRequest { At = At } }).ShouldBeFalse();
        Valid(Message() with { Error = new ContactErrorRequest { Ref = TraceId } }).ShouldBeFalse();
        Valid(Message() with { Error = new ContactErrorRequest { Ref = "<b>", At = At } }).ShouldBeFalse();
        Valid(Message() with { Error = new ContactErrorRequest { Message = "two\nlines", At = At } }).ShouldBeFalse();
        Valid(Message() with { Error = new ContactErrorRequest { Message = new string('x', 301), At = At } }).ShouldBeFalse();
    }
}
