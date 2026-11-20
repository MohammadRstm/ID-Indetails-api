using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Company;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>Everyone's week as the Schedules page lists it, with the company's display settings and the filters in force.</summary>
/// <param name="WeekStart">0 = Sunday, 1 = Monday: the first weekday column.</param>
/// <param name="BothBusinesses">The caller sees both businesses and didn't narrow to one: people with no department name their business.</param>
public sealed record SchedulesReport(
    SchedulesResponse Schedules, DateOnly Today, byte WeekStart, bool Use24Hour, bool BothBusinesses, string? Domain, string? Search);

/// <summary>A heading of the schedules list and the people under it.</summary>
public sealed record ScheduleGroup(string Name, IReadOnlyList<PersonScheduleResponse> People);

/// <summary>
/// Everyone's week on a landscape page, grouped as the Schedules page groups them. Internal, so no watermark. The CSV
/// export (<see cref="SchedulesCsv"/>) lists the people in the same order.
/// </summary>
public static class SchedulesPdf
{
    /// <summary>Sunday first, as the weekday numbers count.</summary>
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static byte[] Render(SchedulesReport report, Letterhead head)
    {
        var footer = $"{head.CompanyName} · Internal";
        var (document, section) = PdfLayout.NewDocument("Schedules", footer, landscape: true);
        PdfLayout.AddLetterhead(section, head, "Schedules", "Generated " + PdfLayout.Date(report.Today), widthCm: PdfLayout.LandscapeContentWidthCm);

        var subtitle = new List<string> { "As of " + PdfLayout.Date(report.Today) };
        if (!string.IsNullOrWhiteSpace(report.Domain))
        {
            subtitle.Add("Business: " + Capitalised(report.Domain));
        }

        if (!string.IsNullOrWhiteSpace(report.Search))
        {
            subtitle.Add($"Search: \"{report.Search.Trim()}\"");
        }

        PdfLayout.AddTitle(section, "Schedules", string.Join(" · ", subtitle));

        var order = WeekOrder(report.WeekStart);
        var week = report.Schedules.CompanyWeek;
        PdfLayout.AddHeading(section, "Company default week");
        PdfLayout.AddFacts(
            section,
            [new Fact("Working days", string.Join(", ", order.Where(d => week.WorkDays.Contains(d)).Select(DayName)))],
            [new Fact("Hours", TimeRange(week.StartMinute, week.EndMinute, report.Use24Hour))],
            [new Fact("Break", week.BreakMinutes > 0 ? PdfLayout.Duration(week.BreakMinutes) + " (paid)" : "None")],
            [new Fact("Required a week", PdfLayout.Duration(week.RequiredMinutesPerWeek))]);

        var columns = new List<TableColumn> { new("Person", 6.0) };
        columns.AddRange(order.Select(d => new TableColumn(DayName(d), 2.6)));
        columns.Add(new TableColumn("Week", 1.9, AlignRight: true));
        var table = PdfLayout.AddTable(section, [.. columns]);

        var people = report.Schedules.People;
        foreach (var group in Groups(people, report.BothBusinesses))
        {
            PdfLayout.AddGroupRow(table, group.Name);
            foreach (var p in group.People)
            {
                var cells = new List<string?> { p.Name };
                cells.AddRange(order.Select(d => p.Days.FirstOrDefault(x => x.Weekday == d) is { } day ? TimeRange(day.StartMinute, day.EndMinute, report.Use24Hour) : "Off"));
                cells.Add(PdfLayout.Duration(p.RequiredMinutesPerWeek));
                var row = PdfLayout.AddRow(table, false, [.. cells]);

                var note = new[] { p.Position, p.IsOwn ? "Own schedule" : "Company default", p.NextChangeOn is { } changeDay ? "Changes on " + PdfLayout.Date(changeDay) : null };
                PdfLayout.AddCellNote(row, 0, string.Join(" · ", note.Where(n => !string.IsNullOrWhiteSpace(n))));
            }
        }

        if (people.Count == 0)
        {
            PdfLayout.AddNote(section, "No one matches.");
        }

        return PdfLayout.Render(document);
    }

    /// <summary>
    /// The page's order: each business in turn (Design, then Execution) with its departments alphabetically and then
    /// the people with none, and last the people who belong to no business (super admins). People are by name.
    /// </summary>
    public static IReadOnlyList<ScheduleGroup> Groups(IReadOnlyList<PersonScheduleResponse> people, bool bothBusinesses)
    {
        var groups = new List<ScheduleGroup>();
        foreach (var domain in DomainCodes.Businesses)
        {
            var inDomain = people.Where(p => p.Domain == domain).ToList();
            foreach (var team in inDomain.Where(p => p.Team is not null).Select(p => p.Team!.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase))
            {
                groups.Add(new ScheduleGroup(team, ByName(inDomain.Where(p => p.Team?.Name == team))));
            }

            var noTeam = inDomain.Where(p => p.Team is null).ToList();
            if (noTeam.Count > 0)
            {
                groups.Add(new ScheduleGroup(bothBusinesses ? $"{Capitalised(domain)} · no department" : "No department", ByName(noTeam)));
            }
        }

        var company = people.Where(p => p.Domain is null).ToList();
        if (company.Count > 0)
        {
            groups.Add(new ScheduleGroup("Company", ByName(company)));
        }

        return groups;
    }

    /// <summary>The weekday numbers (0 = Sunday) in the order the company's week runs, from <paramref name="weekStart"/>.</summary>
    public static IReadOnlyList<int> WeekOrder(byte weekStart) => Enumerable.Range(0, 7).Select(i => (weekStart + i) % 7).ToList();

    public static string DayName(int weekday) => DayNames[weekday];

    /// <summary>9:00–17:00, or 9:00 AM–5:00 PM.</summary>
    public static string TimeRange(int startMinute, int endMinute, bool use24Hour)
        => PdfLayout.TimeOfDay(startMinute, use24Hour) + "–" + PdfLayout.TimeOfDay(endMinute, use24Hour);

    public static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static IReadOnlyList<PersonScheduleResponse> ByName(IEnumerable<PersonScheduleResponse> people)
        => people.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
