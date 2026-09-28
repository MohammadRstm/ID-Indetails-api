namespace ID_Indetails.api.Models;

/// <summary>A day off covered by an <see cref="HourExcuse"/> of kind Days.</summary>
public class HourExcuseDay
{
    public long ExcuseId { get; set; }

    public DateOnly Day { get; set; }

    public HourExcuse Excuse { get; set; } = null!;
}
