namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// What the seeder has created so far and plans next. The API holds the truth; this keeps the ids and the plans the
/// simulation needs to act on them. Lists, not hash sets, so every run walks them in the same order.
/// </summary>
public sealed class World(SeedPlan plan)
{
    public SeedPlan Plan { get; } = plan;

    public Random Random { get; } = new(plan.RandomSeed);

    public List<Person> People { get; } = [];

    public List<(long Id, string Domain)> Teams { get; } = [];

    public List<SeedClient> Clients { get; } = [];

    public List<SeedProject> Projects { get; } = [];

    public List<SeedInvoice> Invoices { get; } = [];

    /// <summary>Design people absent on a day, for the next morning's excuses.</summary>
    public List<(Person Person, DateOnly Day)> Absences { get; } = [];

    public Tally Tally { get; } = new();

    public Person FirstSuperAdmin => People[0];

    public List<Person> Working(DateOnly day, Func<Person, bool> which) => People.Where(p => p.WorksOn(day) && which(p)).ToList();

    public Person AnyAdmin(string domain, DateOnly day) => Random.Pick(Working(day, p => p.IsAdmin && p.Domain == domain));

    public List<SeedProject> Live(string domain) => Projects.Where(p => p.Domain == domain && p.Status == "active").ToList();

    /// <summary>A company working day: Monday to Friday, as the seeded working-time policy says.</summary>
    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static bool IsFirstWorkingDayOfMonth(DateOnly day)
        => IsWorkingDay(day) && Enumerable.Range(1, day.Day - 1).All(d => !IsWorkingDay(new DateOnly(day.Year, day.Month, d)));
}

public sealed record SeedClient(long Id, string Domain, string Name, string Email);

public sealed class SeedProject
{
    public required long Id { get; init; }

    public required string Domain { get; init; }

    public required SeedClient? Client { get; init; }

    public required Person Owner { get; set; }

    public required DateOnly StartedOn { get; init; }

    public required DateOnly TargetOn { get; init; }

    /// <summary>active, hold, completed or archived, as the API last said.</summary>
    public string Status { get; set; } = "active";

    /// <summary>Design: the designers who log work on it. Execution: the site crew.</summary>
    public List<Person> Team { get; } = [];

    public List<SeedRequirement> Requirements { get; } = [];

    public DateOnly? HoldOn { get; set; }

    public DateOnly? ResumeOn { get; set; }

    public DateOnly? CompleteOn { get; set; }

    public DateOnly? ArchiveOn { get; set; }

    public DateOnly LastMeetingOn { get; set; }

    /// <summary>Design client projects: the agreed contract value.</summary>
    public decimal Revenue { get; init; }

    public decimal Invoiced { get; set; }

    /// <summary>Invoiced by its own invoice plan, which the scheduled checks issue.</summary>
    public bool AutoInvoiced { get; set; }

    public decimal Fee { get; init; }

    public int Workers { get; init; }

    public decimal DailyPay { get; init; }

    public DateOnly LastInvoiceOn { get; set; }

    public bool IsClientWork => Client is not null;
}

public sealed record SeedRequirement(long Id, DateOnly DoneOn, IReadOnlyList<Person> Assignees)
{
    public bool Done { get; set; }
}

public sealed class SeedInvoice(long id, SeedProject project, decimal total)
{
    public long Id { get; } = id;

    public SeedProject Project { get; } = project;

    public decimal Total { get; } = total;

    public decimal Paid { get; set; }

    public DateOnly? IssuedOn { get; set; }

    /// <summary>Planned payments: when the client pays, and how much of what is left.</summary>
    public List<(DateOnly On, decimal Amount)> Payments { get; } = [];
}

/// <summary>How many of each thing the seed created, to compare with the database afterwards.</summary>
public sealed class Tally
{
    private readonly SortedDictionary<string, int> _counts = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Counts => _counts;

    public void Add(string what, int count = 1) => _counts[what] = _counts.GetValueOrDefault(what) + count;

    public int this[string what] => _counts.GetValueOrDefault(what);
}
