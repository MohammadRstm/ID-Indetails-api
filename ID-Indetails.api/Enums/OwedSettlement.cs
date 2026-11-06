namespace ID_Indetails.api.Enums;

/// <summary>
/// What became of what a client still owed when its project was completed: all of it, either way. Both lower the price
/// to what was received; they differ in what the company calls it.
/// </summary>
public enum OwedSettlement
{
    /// <summary>Forgiven: a discount agreed with the client.</summary>
    Excused,
    /// <summary>Never paid: revenue the company lost.</summary>
    WrittenOff,
}
