namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// The TVA (value added tax) a client project carries: a rate on what the client pays for the work, collected for the state.
/// Revenue, profit and margins are net of it. What the client sees and owes (invoices, quotes, BOQs, "owed")
/// includes it, never as a line of its own. Each payment records its TVA share, so money reports stay net.
/// </summary>
public static class Tva
{
    /// <summary>A new client project's rate, in every area. A project keeps the rate it was created with.</summary>
    public const decimal DefaultRate = 0.11m;

    public const decimal MaxRate = 1m;

    /// <summary>The TVA on <paramref name="taxable"/>.</summary>
    public static decimal Of(decimal taxable, decimal rate) => Math.Round(taxable * rate, 2, MidpointRounding.AwayFromZero);

    /// <summary>What the client pays for <paramref name="net"/>: TVA included.</summary>
    public static decimal Gross(decimal net, decimal rate) => Math.Round(net * (1 + rate), 2, MidpointRounding.AwayFromZero);

    /// <summary>The TVA inside an amount the client paid.</summary>
    public static decimal ShareOf(decimal gross, decimal rate)
        => rate <= 0 ? 0 : Math.Round(gross * rate / (1 + rate), 2, MidpointRounding.AwayFromZero);
}
