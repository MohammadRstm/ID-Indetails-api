using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>The company's week start and clock format: Monday and 12-hour time until the company settings are saved.</summary>
internal sealed record CompanyCalendar(DayOfWeek FirstDayOfWeek, bool Use24HourTime)
{
    public static CompanyCalendar Of(CompanySettings? settings)
        => settings is null
            ? new CompanyCalendar(DayOfWeek.Monday, false)
            : new CompanyCalendar(settings.WeekStart == 0 ? DayOfWeek.Sunday : DayOfWeek.Monday, settings.Use24HourTime);
}
