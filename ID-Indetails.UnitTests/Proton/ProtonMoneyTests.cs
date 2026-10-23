using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Proton.Tools;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonMoneyTests
{
    private static JsonElement Details(object payload) => JsonSerializer.SerializeToElement(payload);

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
