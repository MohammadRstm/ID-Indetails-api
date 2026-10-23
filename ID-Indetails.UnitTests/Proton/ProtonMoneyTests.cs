using System.Security.Claims;
using System.Text.Json;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonMoneyTests
{
    private static JsonElement Details(object payload) => JsonSerializer.SerializeToElement(payload);

    private static FinanceProjectResponse Project(string title, decimal? margin) => new(
        title.Length, title, null, ProjectStatus.Active, margin is null ? BillingType.Internal : BillingType.Client, 1000, 0, 10,
        new ProjectCostResponse(0, 0, 0, 0, 0, [], 0), 0, margin, margin is not null, null, 0);

    /// <summary>"Lowest margins" means the projects that have one: internal and too-early projects never fill the list.</summary>
    [Fact]
    public void Projects_without_a_margin_rank_last_both_ways()
    {
        FinanceProjectResponse[] projects = [Project("Internal", null), Project("Thin", 0.05m), Project("Early", null), Project("Fat", 0.40m)];

        GetFinanceProjectsTool.Rank(projects, FinanceProjectSort.Margin, ascending: true).Select(p => p.Title)
            .ShouldBe(["Thin", "Fat", "Internal", "Early"]);
        GetFinanceProjectsTool.Rank(projects, FinanceProjectSort.Margin, ascending: false).Select(p => p.Title)
            .ShouldBe(["Fat", "Thin", "Internal", "Early"]);
    }

    private static readonly DateTime Now = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<JsonElement> AddUpAsync(ProtonDataset dataset, object input)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var context = new ProtonToolContext(
            new ClaimsPrincipal(), new UserAccess(1, RoleCodes.SuperAdmin, null, [], false, DomainCodes.All),
            Mock.Of<IAuthorizationService>(), Mock.Of<IServiceProvider>(), DateOnly.FromDateTime(Now), DayOfWeek.Monday);
        context.HoldDataset("list_payments", dataset, Now);
        var tool = new AddUpTool(clock.Object, Options.Create(new ProtonOptions()));
        var output = await tool.RunAsync(JsonSerializer.SerializeToElement(input), context, CancellationToken.None);
        return JsonSerializer.SerializeToElement(output.Data, ProtonJson.Options);
    }

    private static ProtonDataset Payments(int? totalRows = null) => new(
        "Payments",
        [ProtonDatasets.Label("client", "Client"), ProtonDatasets.Figure("amount_usd", "Amount (USD)", ProtonUnit.Usd), ProtonDatasets.Figure("share", "Share", ProtonUnit.Percent)],
        [
            ProtonDatasets.Row(null, ProtonCell.Of("Karim"), ProtonCell.Of(1200m), ProtonCell.Of(60m)),
            ProtonDatasets.Row(null, ProtonCell.Of("Lina"), ProtonCell.Of(800.5m), ProtonCell.Of(40m)),
            ProtonDatasets.Row(null, ProtonCell.Of("karim"), ProtonCell.Of((decimal?)null), ProtonCell.Of(0m)),
        ],
        TotalRows: totalRows);

    /// <summary>The model asks for a total and the server adds the dataset's own values: it never retypes a figure.</summary>
    [Fact]
    public async Task Add_up_totals_a_column_on_the_server_and_says_what_it_left_out()
    {
        var all = await AddUpAsync(Payments(), new { dataset = "d1", column = "amount_usd" });
        all.GetProperty("total").GetDecimal().ShouldBe(2000.5m);
        all.GetProperty("rows").GetInt32().ShouldBe(2);
        all.GetProperty("rows_without_value").GetInt32().ShouldBe(1);
        all.GetProperty("unit").GetString().ShouldBe("usd");

        var karim = await AddUpAsync(Payments(), new { dataset = "d1", column = "amount_usd", where_column = "client", where_value = "KARIM" });
        karim.GetProperty("total").GetDecimal().ShouldBe(1200m);
    }

    [Fact]
    public async Task Add_up_refuses_a_cut_short_dataset_percentages_and_a_text_column()
    {
        (await Should.ThrowAsync<ProtonToolInputException>(AddUpAsync(Payments(totalRows: 250), new { dataset = "d1", column = "amount_usd" })))
            .Message.ShouldContain("only the first 3 of 250 rows");
        (await Should.ThrowAsync<ProtonToolInputException>(AddUpAsync(Payments(), new { dataset = "d1", column = "share" })))
            .Message.ShouldContain("Percentages don't add up");
        (await Should.ThrowAsync<ProtonToolInputException>(AddUpAsync(Payments(), new { dataset = "d1", column = "client" })))
            .Message.ShouldContain("amount_usd");
        (await Should.ThrowAsync<ProtonToolInputException>(AddUpAsync(Payments(), new { dataset = "d9", column = "amount_usd" })))
            .Message.ShouldContain("no dataset");
    }

    /// <summary>
    /// A refusal comes before the service writes anything; any other error may come after its save, so the user and the
    /// model are never told "nothing changed" then (a payment recorded twice).
    /// </summary>
    [Fact]
    public void A_failed_confirmation_says_nothing_changed_only_when_it_was_refused()
    {
        var refused = ProtonActionService.FailureOf(new BusinessRuleException(ErrorCodes.ProtonActionStale, "The draft changed."));
        refused.MaybeSaved.ShouldBeFalse();
        refused.Code.ShouldBe(ErrorCodes.ProtonActionStale);

        ProtonActionService.FailureOf(new FluentValidation.ValidationException("Amount must be more than zero.")).MaybeSaved.ShouldBeFalse();

        var unexpected = ProtonActionService.FailureOf(new InvalidOperationException("connection reset after commit"));
        unexpected.MaybeSaved.ShouldBeTrue();
        unexpected.Code.ShouldBe(ErrorCodes.ProtonActionUncertain);
        unexpected.Message.ShouldContain("may have been saved");
        unexpected.Message.ShouldNotContain("connection");
    }

    [Fact]
    public void An_unconverted_cost_says_its_currency_and_source()
        => ProtonAnalysis.Unconverted([new UnconvertedCostResponse(CostSource.GroundLabour, "LBP", 4_500_000m), new UnconvertedCostResponse(CostSource.Expenses, "LBP", 12.5m)])
            .ShouldBe(["4500000.00 LBP (ground labour)", "12.50 LBP (expenses)"]);

    /// <summary>The feed keeps an amount without its unit; Proton is told it, so 89,500 LBP per USD isn't read as 89,500 USD.</summary>
    [Fact]
    public void An_activity_amount_comes_with_its_unit()
    {
        ActivityRow.UnitOf(ActivityEventKinds.ExchangeRateSet, 89_500m, Details(new { currency = "LBP", effectiveFrom = "2026-10-01" }))
            .ShouldBe("LBP per USD");
        ActivityRow.UnitOf(ActivityEventKinds.ProgressSet, 40m, null).ShouldBe("percent");
        ActivityRow.UnitOf(ActivityEventKinds.ExpenseAdded, 4_500_000m, Details(new { label = "Taxi", category = "travel", currency = "LBP" })).ShouldBe("LBP");
        ActivityRow.UnitOf(ActivityEventKinds.PaymentRecorded, 1_000m, Details(new { method = "bank" })).ShouldBe("USD");
        ActivityRow.UnitOf(ActivityEventKinds.ProjectCreated, null, null).ShouldBeNull();
    }
}
