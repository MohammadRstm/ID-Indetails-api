using ID_Indetails.api.Business.Billing;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// What an Execution client is charged:
/// <list type="bullet">
/// <item>The work price is the project fee, set by agreeing BOQs. The company's supervision is the work price × the
/// supervision rate.</item>
/// <item>The agreed amount, which revenue and profit are measured on, is the work price plus supervision.</item>
/// <item>TVA is on the work price, never on supervision. The client pays the agreed amount plus that TVA.</item>
/// </list>
/// </summary>
public static class ExecutionPricing
{
    /// <summary>A new site's supervision rate, 10% of the work price.</summary>
    public const decimal DefaultSupervisionRate = 0.10m;

    /// <param name="Net">The agreed amount without TVA.</param>
    /// <param name="Tva">The TVA inside <paramref name="Gross"/>.</param>
    /// <param name="Gross">What the client pays: the agreed amount plus TVA.</param>
    public sealed record Price(decimal Supervision, decimal Net, decimal Tva, decimal Gross);

    /// <param name="work">The project fee, 0 until a BOQ is agreed.</param>
    public static Price PriceOf(decimal work, decimal supervisionRate, decimal tvaRate)
    {
        var supervision = Math.Round(work * supervisionRate, 2, MidpointRounding.AwayFromZero);
        var net = work + supervision;
        var tva = Tva.Of(work, tvaRate);
        return new Price(supervision, net, tva, net + tva);
    }

    /// <summary>
    /// The TVA inside a payment: the project's TVA in proportion to what was paid of what the client pays. With no price
    /// agreed yet, it is the plain TVA share of an amount.
    /// </summary>
    public static decimal TvaShareOf(decimal paid, decimal projectTva, decimal projectGross, decimal tvaRate)
        => projectGross <= 0 ? Tva.ShareOf(paid, tvaRate) : Math.Round(paid * projectTva / projectGross, 2, MidpointRounding.AwayFromZero);
}
