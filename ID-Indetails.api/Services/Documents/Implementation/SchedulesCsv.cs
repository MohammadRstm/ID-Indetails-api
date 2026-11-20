using System.Globalization;
using ID_Indetails.api.Business.Common;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>Everyone's week as a spreadsheet: one row per person, in the order the Schedules PDF lists them.</summary>
public static class SchedulesCsv
{
    private static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    /// <summary>Days read 09:00-17:00 (always 24-hour, so a spreadsheet can sort and read them) or Off; hours a week are decimal.</summary>
    public static byte[] Write(SchedulesReport report)
    {
        var order = SchedulesPdf.WeekOrder(report.WeekStart);
        var header = new List<string> { "Name", "Business", "Department", "Position", "Schedule" };
        header.AddRange(order.Select(d => DayNames[d]));
        header.AddRange(["Hours a week", "Next change"]);

        var rows = new List<IReadOnlyList<string>>();
        foreach (var p in SchedulesPdf.Groups(report.Schedules.People, report.BothBusinesses).SelectMany(g => g.People))
        {
            var row = new List<string>
            {
                p.Name,
                p.Domain is { } domain ? SchedulesPdf.Capitalised(domain) : string.Empty,
                p.Team?.Name ?? string.Empty,
                p.Position ?? string.Empty,
                p.IsOwn ? "Own" : "Default",
            };
            row.AddRange(order.Select(d => p.Days.FirstOrDefault(x => x.Weekday == d) is { } day ? Span(day.StartMinute, day.EndMinute) : "Off"));
            row.Add((p.RequiredMinutesPerWeek / 60m).ToString("0.##", CultureInfo.InvariantCulture));
            row.Add(p.NextChangeOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
            rows.Add(row);
        }

        return Csv.Write(header, rows);
    }

    private static string Span(int startMinute, int endMinute) => $"{Clock(startMinute)}-{Clock(endMinute)}";

    private static string Clock(int minute) => $"{minute / 60:00}:{minute % 60:00}";
}
