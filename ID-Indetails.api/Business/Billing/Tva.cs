namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// The TVA (value added tax) a client project carries: a rate on everything the client pays, collected for the state.
/// Revenue, profit and margins are net of it. What the client sees and owes (invoices, statements, quotes, "owed")
/// includes it, never as a line of its own. Each payment records its TVA share, so money reports stay net.
/// </summary>
public static class Tva
{
    /// <summary>A new project's rate. Projects that existed before TVA started at 0.</summary>
    public const decimal DefaultRate = 0.15m;

    public const decimal MaxRate = 1m;

    /// <summary>What the client pays for <paramref name="net"/>: TVA included.</summary>
    public static decimal Gross(decimal net, decimal rate) => Math.Round(net * (1 + rate), 2, MidpointRounding.AwayFromZero);

    /// <summary>The TVA inside an amount the client paid.</summary>
    public static decimal ShareOf(decimal gross, decimal rate)
        => rate <= 0 ? 0 : Math.Round(gross * rate / (1 + rate), 2, MidpointRounding.AwayFromZero);
}
