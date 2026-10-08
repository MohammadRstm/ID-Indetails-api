using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Proton's greeting briefing: get_attention's answer without a model call, for whoever may use Proton.</summary>
public class ProtonBriefingTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<ProtonBriefingResponse> BriefingAsync(AuthResponse who)
    {
        var response = await Client.GetAsync("/api/proton/briefing", who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonBriefingResponse>();
    }

    private async Task LateAsync(long projectId)
    {
        var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20).ToString("yyyy-MM-dd");
        await _steps.ExecuteAsync("UPDATE projects SET start_date = DATE_SUB(@d, INTERVAL 30 DAY), target_date = @d WHERE id = @id", ("@d", target), ("@id", projectId));
    }

    [Fact]
    public async Task The_briefing_is_what_get_attention_finds_first_and_costs_no_question()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await LateAsync((await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design)).Id);

        var briefing = await BriefingAsync(superAdmin);

        briefing.Items.Count.ShouldBeInRange(1, 3);
        api.Anthropic.Requests.ShouldBeEmpty();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
            (await db.ProtonUsage.CountAsync(u => u.UserId == superAdmin.User.Id)).ShouldBe(0);
        }

        // The same items, in the same order, as the tool gives the model.
        api.Anthropic.Reply(FakeAnthropic.ToolUse("get_attention", new { limit = 3 }), FakeAnthropic.Text("Here is what stands out."));
        (await Client.PostJsonAsync("/api/proton/messages", new { text = "What should I look at?" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var tool = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!["data"]!;
        var items = tool["items"]!.AsArray();
        briefing.Items.Select(i => i.Project?.Id).ShouldBe(items.Select(i => i!["project"]?["id"]?.GetValue<long>()));
        briefing.Items[0].Facts.ShouldBe(items[0]!["signals"]!.AsArray().Select(s => s!["fact"]!.GetValue<string>()));
        briefing.More.ShouldBe(tool["more"]!.GetValue<int>());
    }

    [Fact]
    public async Task Each_person_is_briefed_on_what_they_may_see_and_employees_not_at_all()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await LateAsync((await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution)).Id);

        var briefing = await BriefingAsync(designAdmin);

        briefing.Items.ShouldAllBe(i => i.Domain != DomainCodes.Execution);
        await (await Client.GetAsync("/api/proton/briefing", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
