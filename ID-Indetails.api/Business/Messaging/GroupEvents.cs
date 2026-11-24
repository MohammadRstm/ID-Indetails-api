namespace ID_Indetails.api.Business.Messaging;

/// <summary>
/// The words of a custom group's grey lines, after the name of whoever made the change ("Rami Khoury added Sara Haddad").
/// Names are kept as they were then, as the rest of a chat's history is.
/// </summary>
public static class GroupEvents
{
    public static string Created(string name) => $"created the group \u201c{name}\u201d";

    public static string Renamed(string name) => $"renamed the group to \u201c{name}\u201d";

    public static string Added(IReadOnlyList<string> names) => $"added {List(names)}";

    public static string Removed(string name) => $"removed {name}";

    public const string Left = "left the group";

    public const string PhotoChanged = "changed the group photo";

    public const string PhotoRemoved = "removed the group photo";

    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string List(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}
