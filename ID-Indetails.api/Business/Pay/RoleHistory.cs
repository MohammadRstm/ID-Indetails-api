namespace ID_Indetails.api.Business.Pay;

/// <summary>
/// The roles someone held over time, from their recorded role changes: every change, manual or automatic, records the
/// role it changed from and to. A change counts from its day, in the company time zone. Before the first change they
/// held the role it changed from; someone whose role never changed has held their current role throughout.
/// </summary>
/// <param name="Changes">Oldest first; changes on the same day in the order they happened.</param>
public sealed record RoleHistory(string CurrentRole, IReadOnlyList<RoleHistory.Change> Changes)
{
    public sealed record Change(DateOnly Day, string From, string To);

    public string RoleOn(DateOnly day)
        => Changes.LastOrDefault(c => c.Day <= day)?.To ?? (Changes.Count > 0 ? Changes[0].From : CurrentRole);

    /// <summary>Whether they were an admin or super admin that day, paid a fixed salary (<see cref="PayRules.HasFixedSalary"/>).</summary>
    public bool HasFixedSalaryOn(DateOnly day) => PayRules.HasFixedSalary(RoleOn(day));

    /// <summary>Whether they were an admin or super admin on any day from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool HadFixedSalary(DateOnly from, DateOnly to)
        => HasFixedSalaryOn(from) || Changes.Any(c => c.Day > from && c.Day <= to && PayRules.HasFixedSalary(c.To));

    /// <summary>
    /// Whether the month containing <paramref name="day"/> is paid as a fixed salary: they were an admin or super admin on
    /// at least one of its days. So a promotion makes its whole month fixed, a demoted admin keeps the fixed salary until
    /// the month ends, and the months before and after keep their own role.
    /// </summary>
    public bool FixedSalaryMonth(DateOnly day)
    {
        var month = new DateOnly(day.Year, day.Month, 1);
        return HadFixedSalary(month, month.AddMonths(1).AddDays(-1));
    }
}
