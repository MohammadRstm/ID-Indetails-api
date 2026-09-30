namespace ID_Indetails.api.Business.Clients;

/// <summary>Clients on the books month by month, as in the prototype's client growth chart (archived ones included).</summary>
public static class ClientGrowth
{
    public const int Months = 13;

    /// <param name="On">The company day the client was added.</param>
    public sealed record Added(long Id, string Name, DateOnly On);

    /// <param name="Total">Clients added by the end of the month.</param>
    public sealed record Month(DateOnly Start, int Total, IReadOnlyList<Added> AddedInMonth);

    /// <summary>The last <paramref name="months"/> months, ending with this one, oldest first.</summary>
    public static IReadOnlyList<Month> Of(IReadOnlyCollection<Added> clients, DateOnly today, int months = Months)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        return Enumerable.Range(0, months)
            .Select(i => thisMonth.AddMonths(i - months + 1))
            .Select(start =>
            {
                var next = start.AddMonths(1);
                return new Month(
                    start,
                    clients.Count(c => c.On < next),
                    clients.Where(c => c.On >= start && c.On < next).OrderBy(c => c.On).ThenBy(c => c.Id).ToList());
            })
            .ToList();
    }
}
