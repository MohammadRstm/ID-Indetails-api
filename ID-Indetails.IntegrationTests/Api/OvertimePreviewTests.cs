using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Payroll;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Beirut is UTC+3 in June; Monday 1 June 2026 runs 09:00 to 17:00 with 7 required hours.</summary>
public class OvertimePreviewTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task The_log_form_sees_the_overtime_of_what_it_is_about_to_save()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        // 09:00 to 16:00 already logged: the day's 7 hours are in.
        await new WorkEntryTestSteps(_steps).InsertAsync(designer.User.Id, project.Id, "2026-06-01 06:00:00", "2026-06-01 13:00:00");
        var logged = await _steps.ScalarAsync<long>("SELECT id FROM work_entries WHERE user_id = @id", ("@id", designer.User.Id));

        // 16:00 to 17:30: the hour to 17:00 is past what the day asks for, the half hour after it is after hours.
        var draft = new { startedAt = "2026-06-01T13:00:00Z", endedAt = "2026-06-01T14:30:00Z" };
        var preview = await PreviewAsync(designer, draft);
        preview.ShouldBe(new OvertimePreviewResponse(90, 90, false, 0, 30, 60, 540, 1020, 420));

        // Editing the logged entry into that slot leaves the day's hours short again: only the half hour is overtime.
        (await PreviewAsync(designer, new { draft.startedAt, draft.endedAt, entryId = logged })).OvertimeMinutes.ShouldBe(30);

        // Moving the logged entry to the evening frees the day: the draft is inside hours.
        (await PreviewAsync(designer, new
        {
            startedAt = "2026-06-01T07:00:00Z",
            endedAt = "2026-06-01T08:00:00Z",
            moves = new[] { new { id = logged, startedAt = "2026-06-01T15:00:00Z", endedAt = "2026-06-01T16:00:00Z" } },
        })).OvertimeMinutes.ShouldBe(0);

        // Deleting the logged entry on the timeline frees the day too.
        (await PreviewAsync(designer, new { draft.startedAt, draft.endedAt, deletes = new[] { logged } })).OvertimeMinutes.ShouldBe(30);

        var saturday = await PreviewAsync(designer, new { startedAt = "2026-06-06T07:00:00Z", endedAt = "2026-06-06T08:00:00Z" });
        saturday.DayOff.ShouldBeTrue();
        saturday.OvertimeMinutes.ShouldBe(60);

        await (await Client.PostJsonAsync("/api/me/overtime-preview", new { startedAt = draft.endedAt, endedAt = draft.startedAt }, designer.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/me/overtime-preview", draft, siteWorker.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    private async Task<OvertimePreviewResponse> PreviewAsync(AuthResponse person, object body)
    {
        var response = await Client.PostJsonAsync("/api/me/overtime-preview", body, person.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OvertimePreviewResponse>();
    }
}
