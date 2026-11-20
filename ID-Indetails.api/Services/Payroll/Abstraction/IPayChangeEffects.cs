using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>
/// What someone's pay moves from a day on, as things stand: each month's salary and advance repayment, and the labour and
/// profit of the projects they worked on since. A pay change measures it before and after it is made, inside its
/// transaction, so the preview and the profit floor see exactly what lands.
/// </summary>
/// <param name="Months">Each month from the day's month to this one.</param>
/// <param name="Projects">The projects they logged work on from the day.</param>
public sealed record PayEffects(IReadOnlyList<PayEffects.Month> Months, IReadOnlyList<PayEffects.ProjectMoney> Projects)
{
    /// <param name="Salary">What Salaries says the month is owed; null when no pay covers it.</param>
    /// <param name="Advance">What advances take from the month's pay, in <paramref name="AdvanceCurrency"/>.</param>
    public sealed record Month(DateOnly Start, decimal? Salary, string? Currency, decimal Advance, string? AdvanceCurrency);

    /// <param name="Labour">The project's labour cost, in USD.</param>
    /// <param name="Profit">The project's profit, in USD.</param>
    /// <param name="HeldToFloor">A running client project with an agreed amount, which the profit floor holds to.</param>
    public sealed record ProjectMoney(long ProjectId, string Title, string Domain, decimal Labour, decimal Profit, bool HeldToFloor);
}

public interface IPayChangeEffects
{
    Task<PayEffects> MeasureAsync(long userId, DateOnly from, CancellationToken cancellationToken = default);
}
