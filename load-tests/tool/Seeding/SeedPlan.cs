namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// How big the seeded company is and what period it covers. The mix of people is the expected company's, scaled:
/// at 200 users, 4 super admins, 8 Design admins, 8 Execution admins, 108 Design and 72 Execution employees.
/// The history ends the day before <see cref="Anchor"/>, so the anchor day itself is free for the load test.
/// </summary>
public sealed record SeedPlan(int Users, int Months, int RandomSeed, DateOnly Anchor)
{
    public const int MinimumUsers = 10;

    public int SuperAdmins => Math.Max(1, Share(0.02));

    public int DesignAdmins => Math.Max(1, Share(0.04));

    public int ExecutionAdmins => Math.Max(1, Share(0.04));

    public int DesignEmployees => Math.Max(1, Share(0.54));

    public int ExecutionEmployees => Users - SuperAdmins - DesignAdmins - ExecutionAdmins - DesignEmployees;

    /// <summary>The first day of the month <see cref="Months"/> before the anchor's: when the company starts using Hourline.</summary>
    public DateOnly FirstDay => new DateOnly(Anchor.Year, Anchor.Month, 1).AddMonths(-Months);

    public DateOnly LastDay => Anchor.AddDays(-1);

    private int Share(double fraction) => (int)Math.Round(Users * fraction, MidpointRounding.AwayFromZero);
}
