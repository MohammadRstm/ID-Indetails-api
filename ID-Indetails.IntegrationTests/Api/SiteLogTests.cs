using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class SiteLogTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<SiteLogResponse> WriteAsync(long projectId, string body, string token, long? voiceFileId = null)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/site-logs", new { body, voiceFileId }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<SiteLogResponse>();
    }

    private async Task<PagedResponse<SiteLogResponse>> ListAsync(string url, string token)
    {
        var response = await Client.GetAsync(url, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PagedResponse<SiteLogResponse>>();
    }

    [Fact]
    public async Task Site_staff_write_several_logs_a_day_each_kept_apart_newest_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id]);

        var concrete = await WriteAsync(site.Id, "  Concrete shipment arrived with 3 bags missing.  ", rami.AccessToken);
        concrete.Body.ShouldBe("Concrete shipment arrived with 3 bags missing.");
        concrete.Person.Id.ShouldBe(rami.User.Id);
        concrete.Project.ShouldBe(new SiteLogProjectResponse(site.Id, site.Title));
        concrete.CanEdit.ShouldBeTrue();
        var glue = await WriteAsync(site.Id, "The glue shipment is delayed until tomorrow.", rami.AccessToken);

        var mine = await ListAsync("/api/me/site-logs", rami.AccessToken);
        mine.TotalCount.ShouldBe(2);
        mine.Items.Select(l => l.Id).ShouldBe([glue.Id, concrete.Id]);

        // found by its words, its site and its day
        (await ListAsync("/api/me/site-logs?search=glue", rami.AccessToken)).Items.Select(l => l.Id).ShouldBe([glue.Id]);
        (await ListAsync($"/api/me/site-logs?projectId={site.Id}&pageSize=1", rami.AccessToken)).ShouldSatisfyAllConditions(
            p => p.TotalCount.ShouldBe(2), p => p.Items.Count.ShouldBe(1));
        var today = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);
        (await ListAsync($"/api/me/site-logs?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}", rami.AccessToken)).TotalCount.ShouldBe(2);
        (await ListAsync($"/api/me/site-logs?from={today.AddDays(-9):yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}", rami.AccessToken)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_log_is_words_a_voice_note_or_both_and_lists_show_the_voice_note()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id]);

        var memo = await FileTests.UploadVoiceNoteAsync(Client, rami.AccessToken);
        var spoken = await WriteAsync(site.Id, "   ", rami.AccessToken, memo.Id);
        spoken.Body.ShouldBe(string.Empty);
        var voice = spoken.VoiceNote.ShouldNotBeNull();
        (voice.Id, voice.Kind, voice.MimeType).ShouldBe((memo.Id, FileKind.Audio, "audio/webm"));

        var recording = await FileTests.UploadVoiceNoteAsync(Client, rami.AccessToken, FileTests.OggVoice, "crack.ogg");
        var both = await WriteAsync(site.Id, "  A crack in the slab.  ", rami.AccessToken, recording.Id);
        both.Body.ShouldBe("A crack in the slab.");
        both.VoiceNote!.Id.ShouldBe(recording.Id);
        var written = await WriteAsync(site.Id, "Only words.", rami.AccessToken);
        written.VoiceNote.ShouldBeNull();

        // lists carry the voice note of each log
        var mine = await ListAsync("/api/me/site-logs", rami.AccessToken);
        mine.Items.Select(l => (l.Id, Voice: l.VoiceNote?.Id)).ShouldBe([(written.Id, (long?)null), (both.Id, recording.Id), (spoken.Id, memo.Id)]);
        var everyone = await ListAsync($"/api/site-logs?projectId={site.Id}", superAdmin.AccessToken);
        everyone.Items.Single(l => l.Id == spoken.Id).VoiceNote!.Id.ShouldBe(memo.Id);
    }

    [Fact]
    public async Task A_log_with_neither_words_nor_a_voice_note_is_refused_and_the_voice_note_must_be_the_writers_own_recording()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id, layla.User.Id]);
        var url = $"/api/projects/{site.Id}/site-logs";

        var empty = await (await Client.PostJsonAsync(url, new { body = " ", voiceFileId = (long?)null }, rami.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        empty.Errors!["body"].ShouldContain("Write what happened or record a voice note.");

        // someone else's upload reads as not found; a photo is not a recording
        var theirs = await FileTests.UploadVoiceNoteAsync(Client, layla.AccessToken);
        await (await Client.PostJsonAsync(url, new { body = "", voiceFileId = theirs.Id }, rami.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        var photo = await FileTests.UploadPngAsync(Client, rami.AccessToken);
        var notAudio = await (await Client.PostJsonAsync(url, new { body = "", voiceFileId = photo.Id }, rami.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);
        notAudio.Message.ShouldBe("A voice note must be a recording.");
        await (await Client.PostJsonAsync(url, new { body = "", voiceFileId = 0 }, rami.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await ListAsync("/api/me/site-logs", rami.AccessToken)).TotalCount.ShouldBe(0);

        // the same goes for a change: words or a voice note must stay
        var memo = await FileTests.UploadVoiceNoteAsync(Client, rami.AccessToken);
        var log = await WriteAsync(site.Id, "", rami.AccessToken, memo.Id);
        await (await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body = "  ", voiceFileId = (long?)null }, rami.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body = "", voiceFileId = theirs.Id }, rami.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_voice_note_is_kept_removed_or_replaced_when_its_writer_changes_the_log()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id]);
        var first = await FileTests.UploadVoiceNoteAsync(Client, rami.AccessToken);
        var log = await WriteAsync(site.Id, "", rami.AccessToken, first.Id);

        async Task<SiteLogResponse> ChangeAsync(string body, long? voiceFileId)
        {
            var response = await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body, voiceFileId }, rami.AccessToken);
            ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
            return await response.ReadAsync<SiteLogResponse>();
        }

        // the same id keeps it, whatever the words do
        var kept = await ChangeAsync("  Slab poured.  ", first.Id);
        kept.Body.ShouldBe("Slab poured.");
        kept.VoiceNote!.Id.ShouldBe(first.Id);
        kept.UpdatedAt.ShouldNotBeNull();

        // a new id replaces it
        var second = await FileTests.UploadVoiceNoteAsync(Client, rami.AccessToken, FileTests.M4aVoice, "again.m4a");
        (await ChangeAsync("Slab poured.", second.Id)).VoiceNote!.Id.ShouldBe(second.Id);

        // null removes it, and the words stay
        var removed = await ChangeAsync("Slab poured.", null);
        removed.VoiceNote.ShouldBeNull();
        removed.Body.ShouldBe("Slab poured.");
        (await ListAsync("/api/me/site-logs", rami.AccessToken)).Items.Single().VoiceNote.ShouldBeNull();
    }

    [Fact]
    public async Task A_log_is_changed_by_its_writer_only_on_the_day_it_was_written()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id, layla.User.Id]);
        var log = await WriteAsync(site.Id, "Concrete arrived.", rami.AccessToken);

        var changed = await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body = "Concrete arrived, 3 bags short." }, rami.AccessToken);
        ((int)changed.StatusCode).ShouldBe(200, await changed.Content.ReadAsStringAsync());
        var edited = await changed.ReadAsync<SiteLogResponse>();
        edited.Body.ShouldBe("Concrete arrived, 3 bags short.");
        edited.UpdatedAt.ShouldNotBeNull();

        // someone else's log isn't theirs to change
        await (await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body = "Mine now" }, layla.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // the next day it stands
        await _steps.ExecuteAsync("UPDATE site_logs SET created_at = DATE_SUB(created_at, INTERVAL 2 DAY) WHERE id = @id", ("@id", log.Id));
        await (await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body = "Too late" }, rami.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SiteLogLocked);
        (await ListAsync("/api/me/site-logs", rami.AccessToken)).Items.Single().CanEdit.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_a_sites_staff_write_on_an_open_site_and_a_log_needs_words()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id]);
        var url = $"/api/projects/{site.Id}/site-logs";

        await (await Client.PostJsonAsync(url, new { body = "Not my site" }, layla.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.NotOnSiteTeam);
        await (await Client.PostJsonAsync(url, new { body = "Design" }, designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await (await Client.PostJsonAsync(url, new { body = "   " }, rami.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed))
            .Errors!.Keys.ShouldContain("body");
        await (await Client.PostJsonAsync(url, new { body = new string('x', SiteTeamRules.MaxLogLength + 1) }, rami.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // taken off the site, they write no more, and their earlier logs stay
        await WriteAsync(site.Id, "Last day here.", rami.AccessToken);
        await Projects.PutOnSiteAsync(superAdmin.AccessToken, site.Id, layla.User.Id);
        (await Client.PutJsonAsync($"/api/projects/{site.Id}/staff", new { staffIds = new[] { layla.User.Id } }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync(url, new { body = "Still here?" }, rami.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.NotOnSiteTeam);
        (await ListAsync("/api/me/site-logs", rami.AccessToken)).TotalCount.ShouldBe(1);

        // a finished site takes no logs
        (await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync(url, new { body = "After the end" }, layla.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
    }

    [Fact]
    public async Task Admins_read_every_sites_logs_by_person_and_site_and_staff_read_only_their_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var tower = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id, layla.User.Id]);
        var villa = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [layla.User.Id]);
        var ramiLog = await WriteAsync(tower.Id, "Scaffolding up on the north side.", rami.AccessToken);
        var laylaTower = await WriteAsync(tower.Id, "Crane inspection passed.", layla.AccessToken);
        var laylaVilla = await WriteAsync(villa.Id, "Tiles delivered, two boxes cracked.", layla.AccessToken);

        (await ListAsync($"/api/site-logs?projectId={tower.Id}", siteAdmin.AccessToken)).Items.Select(l => l.Id).ShouldBe([laylaTower.Id, ramiLog.Id]);
        (await ListAsync($"/api/site-logs?userId={layla.User.Id}", siteAdmin.AccessToken)).Items.Select(l => l.Id).ShouldBe([laylaVilla.Id, laylaTower.Id]);
        (await ListAsync($"/api/site-logs?userId={layla.User.Id}&search=cracked", superAdmin.AccessToken)).Items.Select(l => l.Id).ShouldBe([laylaVilla.Id]);
        (await ListAsync($"/api/site-logs?projectId={tower.Id}", siteAdmin.AccessToken)).Items.ShouldAllBe(l => !l.CanEdit);

        // staff read their own, never everyone's; Design doesn't see Execution's logs
        await (await Client.GetAsync("/api/site-logs", rami.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await ListAsync("/api/me/site-logs", rami.AccessToken)).Items.Select(l => l.Id).ShouldBe([ramiLog.Id]);
        await (await Client.GetAsync("/api/site-logs", designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
