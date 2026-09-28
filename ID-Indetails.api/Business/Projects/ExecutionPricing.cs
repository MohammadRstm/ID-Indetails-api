namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// What an Execution client is charged:
/// <list type="bullet">
/// <item>A material's client price is the price someone set by hand, otherwise its cost × (1 + markup),
/// rounded to cents.</item>
/// <item>The agreed amount is the material client prices plus the project fee. Ground labour is never billed.</item>
/// </list>
/// </summary>
public static class ExecutionPricing
{
    public sealed record Material(decimal Cost, decimal? ManualClientPrice);

    public static decimal ClientPrice(Material material, decimal markup)
        => material.ManualClientPrice ?? Math.Round(material.Cost * (1 + markup), 2, MidpointRounding.AwayFromZero);

    public static decimal MaterialCharge(IEnumerable<Material> materials, decimal markup)
        => materials.Sum(m => ClientPrice(m, markup));

    public static decimal AgreedAmount(IEnumerable<Material> materials, decimal markup, decimal projectFee)
        => MaterialCharge(materials, markup) + projectFee;
}
