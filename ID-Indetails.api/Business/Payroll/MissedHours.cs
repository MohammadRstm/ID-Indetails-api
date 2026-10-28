using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Payroll;

public static class MissedHours
{
    public sealed record Short(int LoggedMinutes, int RequiredMinutes);

    public static DateOnly DayToCheck(DateTime companyNow) => throw new NotImplementedException();

    public static Short? Check(
        DateOnly day,
        PersonCalendar calendar,
        Employment employment,
        IReadOnlyList<SalaryCalculator.Excuse> excuses,
        int loggedMinutes)
        => throw new NotImplementedException();
}
