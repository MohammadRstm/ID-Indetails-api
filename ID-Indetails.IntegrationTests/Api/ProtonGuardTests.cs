using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The read-only guarantee beyond database commands: tools that try to send an email, notify someone, or stage a change
/// for a later save are refused, and nothing escapes. The probes stand in for any service a tool might call.
/// </summary>
public class ProtonGuardTests(ProtonProbeApiFactory api) : IClassFixture<ProtonProbeApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    private JsonNode Result(int request) => JsonNode.Parse(api.Anthropic.ToolResults(request).Single()["content"]!.GetValue<string>())!;

    [Fact]
    public async Task A_tool_can_send_no_email_notify_nobody_and_stage_nothing_for_a_later_save()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("probe_send_email", new { }),
            FakeAnthropic.ToolUse("probe_notify", new { }),
            FakeAnthropic.ToolUse("probe_stage_change", new { }),
            // a proposal right after saves its own row: the staged team must not ride along
            FakeAnthropic.ToolUse("propose_add_requirement", new { project_id = project.Id, text = "Check the guard" }),
            FakeAnthropic.Text("Done."));

        var response = await _steps.Client.PostJsonAsync("/api/proton/messages", new { text = "Try everything" }, superAdmin.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());

        Result(1)["status"]!.GetValue<string>().ShouldBe("failed");
        Result(2)["status"]!.GetValue<string>().ShouldBe("failed");
        Result(3)["status"]!.GetValue<string>().ShouldBe("failed");
        Result(4)["status"]!.GetValue<string>().ShouldBe("proposed");
        api.Emails.CountTo(ProbeTools.Address).ShouldBe(0);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        (await db.Teams.CountAsync(t => t.Name == ProbeTools.TeamName)).ShouldBe(0);
        (await db.Notifications.CountAsync(n => n.Kind == ProbeTools.NotificationKind)).ShouldBe(0);
        (await db.ProtonActions.CountAsync(a => a.UserId == superAdmin.User.Id)).ShouldBe(1);
    }

    /// <summary>
    /// A send, delete, money or access change can't start from a click: the options could have been steered by text in
    /// the data. The same request typed by the user is proposed as usual (and still needs its Confirm).
    /// </summary>
    [Fact]
    public async Task A_picked_option_cannot_start_a_gated_change_but_the_user_typing_it_can()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var projects = new ProjectTestSteps(_steps);
        var alpha = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var beta = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        Task<HttpResponseMessage> Ask(object body) => _steps.Client.PostJsonAsync("/api/proton/messages", body, superAdmin.AccessToken);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("find", new { text = alpha.Title }),
            FakeAnthropic.ToolUse("find", new { text = beta.Title }),
            FakeAnthropic.ToolUse("offer_choices", new { question = "Which one?", options = new[] { new { type = "project", id = alpha.Id }, new { type = "project", id = beta.Id } } }),
            FakeAnthropic.Text("Which one?"),
            FakeAnthropic.ToolUse("propose_probe_change", new { }),
            FakeAnthropic.Text("I can't do that from a click."),
            FakeAnthropic.ToolUse("propose_probe_change", new { }),
            FakeAnthropic.Text("Confirm it on the card."));

        var offered = await (await Ask(new { text = "Which project?" })).ReadAsync<ID_Indetails.api.DTO.Response.Proton.ProtonReplyResponse>();
        var picked = await Ask(new { conversationId = offered.ConversationId, choice = new { type = "project", id = beta.Id } });
        picked.IsSuccessStatusCode.ShouldBeTrue(await picked.Content.ReadAsStringAsync());
        Result(5)["status"]!.GetValue<string>().ShouldBe("refused");
        Result(5)["message"]!.GetValue<string>().ShouldContain("in their own words");

        var typed = await Ask(new { conversationId = offered.ConversationId, text = "Make the probe change" });
        typed.IsSuccessStatusCode.ShouldBeTrue(await typed.Content.ReadAsStringAsync());
        Result(7)["status"]!.GetValue<string>().ShouldBe("proposed");
    }

    [Fact]
    public async Task Outside_a_tool_the_guards_change_nothing()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEmailSender>()
            .SendAsync(new EmailMessage(ProbeTools.Address, null, "Hello", "<p>Hi</p>", "Hi"));

        api.Emails.CountTo(ProbeTools.Address).ShouldBe(1);
    }
}

/// <summary>The Proton API with three probe tools that each try something a read must never do.</summary>
public sealed class ProtonProbeApiFactory : ProtonApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IProtonTool, ProbeTools.SendEmail>();
            services.AddScoped<IProtonTool, ProbeTools.Notify>();
            services.AddScoped<IProtonTool, ProbeTools.StageChange>();
            services.AddScoped<IProtonTool, ProbeTools.GatedChange>();
        });
    }
}

public static class ProbeTools
{
    public const string Address = "probe@example.com";
    public const string TeamName = "Probe team";
    public const string NotificationKind = "probe";

    public sealed record NoInput;

    public abstract class Probe : ProtonTool<NoInput>
    {
        public override string Description => "A test probe that tries a side effect.";

        public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));
    }

    public sealed class SendEmail(IEmailSender emails) : Probe
    {
        public override string Name => "probe_send_email";

        protected override async Task<ProtonToolOutput> RunAsync(NoInput input, ProtonToolContext context, CancellationToken cancellationToken)
        {
            await emails.SendAsync(new EmailMessage(Address, null, "Probe", "<p>Probe</p>", "Probe"), cancellationToken);
            return ProtonToolOutput.Of("sent");
        }
    }

    public sealed class Notify(INotificationSender notifications) : Probe
    {
        public override string Name => "probe_notify";

        protected override async Task<ProtonToolOutput> RunAsync(NoInput input, ProtonToolContext context, CancellationToken cancellationToken)
        {
            await notifications.SendAsync(new NotificationDraft(NotificationKind, null, null, null, null, null, null), [1], cancellationToken);
            return ProtonToolOutput.Of("notified");
        }
    }

    public sealed class StageChange(IdIndetailsDbContext db) : Probe
    {
        public override string Name => "probe_stage_change";

        protected override Task<ProtonToolOutput> RunAsync(NoInput input, ProtonToolContext context, CancellationToken cancellationToken)
        {
            db.Teams.Add(new Team { DomainCode = DomainCodes.Design, Name = TeamName, CreatedAt = DateTime.UtcNow });
            return Task.FromResult(ProtonToolOutput.Of("staged"));
        }
    }

    public sealed record GatedInput : ProtonActionInput;

    public sealed record GatedArgs(string Note);

    /// <summary>An action with no coverage entry, so it is held to the strictest rule: only a typed turn may propose it.</summary>
    public sealed class GatedChange : ProtonActionTool<GatedInput, GatedArgs>
    {
        public override string Name => "propose_probe_change";

        public override string Description => "A test probe for an action only a typed turn may propose.";

        public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

        public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<TeamsController>(nameof(TeamsController.Delete));

        protected override Task<(GatedArgs Args, ProtonPreview Preview, ProtonRef? Target)> ProposeAsync(
            GatedInput input, ProtonToolContext context, CancellationToken cancellationToken)
            => Task.FromResult<(GatedArgs, ProtonPreview, ProtonRef?)>(
                (new GatedArgs("probe"), new ProtonPreview("Probe change", [new ProtonChange("Probe", null, "yes")]), null));

        protected override object? RequestOf(GatedArgs args) => null;

        protected override Task<ProtonActionDone> ExecuteAsync(GatedArgs args, ProtonToolContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The probe is never confirmed.");
    }
}
