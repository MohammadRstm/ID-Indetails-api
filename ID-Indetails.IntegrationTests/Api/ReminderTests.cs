using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Repositories.Labour.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Who the labour-week reminder is for: the sites someone recorded on lately whose week isn't reported.</summary>
public class ReminderTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<IReadOnlyList<SiteToReport>> UnreportedAsync(long userId, DateOnly weekStart)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILabourRepository>()
            .UnreportedSitesAsync([userId], DateTime.UtcNow.AddDays(-28), weekStart);
    }

    [Fact]
    public async Task A_site_someone_recorded_on_is_theirs_to_report_until_its_week_is_reported()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, worker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var untouched = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var weekStart = LabourRules.WeekStartOf(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut"))));

        var added = await Client.PostJsonAsync($"/api/projects/{site.Id}/materials", new { items = new[] { new { name = "Cement", amount = 120m } } }, worker.AccessToken);
        added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync());

        var due = await UnreportedAsync(worker.User.Id, weekStart);
        due.ShouldHaveSingleItem().ShouldBe(new SiteToReport(worker.User.Id, site.Id, site.Title));
        due.ShouldNotContain(s => s.ProjectId == untouched.Id);

        var reported = await Client.PutJsonAsync($"/api/projects/{site.Id}/labour-weeks/{weekStart:yyyy-MM-dd}",
            new { days = new[] { new { date = weekStart, workers = 4, dailyPay = 25m } }, note = (string?)null }, worker.AccessToken);
        reported.IsSuccessStatusCode.ShouldBeTrue(await reported.Content.ReadAsStringAsync());

        (await UnreportedAsync(worker.User.Id, weekStart)).ShouldBeEmpty();
        (await UnreportedAsync(worker.User.Id, weekStart.AddDays(7))).ShouldHaveSingleItem().ProjectId.ShouldBe(site.Id);
    }
}
