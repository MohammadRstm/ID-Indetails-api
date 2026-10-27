namespace ID_Indetails.api.Enums;

/// <summary>
/// What happens to a project's money in the box when it closes (<see cref="Remove"/> or <see cref="Keep"/>) or when a
/// removed project reopens (<see cref="Readd"/> its earlier revenue, or count it <see cref="Fresh"/> from today).
/// </summary>
public enum BoxChoice
{
    Remove,
    Keep,
    Readd,
    Fresh,
}
