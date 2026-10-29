using ID_Indetails.api.Business.Billing;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// What an Execution client is charged:
/// <list type="bullet">
/// <item>The work price is the project fee. The company's supervision is the work price × the supervision rate.</item>
/// <item>The agreed amount, which revenue and profit are measured on, is the work price plus supervision. A material's
/// client price (still billed until materials become plain costs) is added while it is.</item>
/// <item>TVA is on the work price, never on supervision. The client pays the agreed amount plus that TVA.</item>
/// <item>A material's client price is the price someone set by hand, otherwise its cost × (1 + markup), rounded to
/// cents. Ground labour is never billed.</item>
/// </list>
/// </summary>
public static class ExecutionPricing
{
    /// <summary>A new site's supervision rate, 10% of the work price.</summary>
    public const decimal DefaultSupervisionRate = 0.10m;

    public sealed record Material(decimal Cost, decimal? ManualClientPrice);

    /// <param name="Net">The agreed amount without TVA.</param>
    /// <param name="Tva">The TVA inside <paramref name="Gross"/>.</param>
    /// <param name="Gross">What the client pays: the agreed amount plus TVA.</param>
    public sealed record Price(decimal Supervision, decimal Net, decimal Tva, decimal Gross);

    public static decimal ClientPrice(Material material, decimal markup)
        => material.ManualClientPrice ?? Math.Round(material.Cost * (1 + markup), 2, MidpointRounding.AwayFromZero);

    public static decimal MaterialCharge(IEnumerable<Material> materials, decimal markup)
        => materials.Sum(m => ClientPrice(m, markup));

    /// <param name="work">The project fee, 0 until a price is agreed.</param>
    /// <param name="materials">The client prices of the materials billed to the client.</param>
    public static Price PriceOf(decimal work, decimal materials, decimal supervisionRate, decimal tvaRate)
    {
        var supervision = Math.Round(work * supervisionRate, 2, MidpointRounding.AwayFromZero);
        var net = work + materials + supervision;
        var tva = Tva.Of(work + materials, tvaRate);
        return new Price(supervision, net, tva, net + tva);
    }

    /// <summary>
    /// The TVA inside a payment: the project's TVA in proportion to what was paid of what the client pays. With no price
    /// agreed yet, it is the plain TVA share of an amount.
    /// </summary>
    public static decimal TvaShareOf(decimal paid, decimal projectTva, decimal projectGross, decimal tvaRate)
        => projectGross <= 0 ? Tva.ShareOf(paid, tvaRate) : Math.Round(paid * projectTva / projectGross, 2, MidpointRounding.AwayFromZero);
}
