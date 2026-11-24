namespace ID_Indetails.api.Constants;

/// <summary>Values of <c>messages.kind</c>.</summary>
public static class MessageKinds
{
    public const string Text = "text";

    /// <summary>A work-entry comment shared to the entry author's team chat, mentioning everyone in it.</summary>
    public const string TaskComment = "task_comment";

    /// <summary>
    /// A grey line in a custom group saying what changed and who did it (its sender): the group made or renamed, its
    /// photo changed, people added or removed, someone leaving. Its body is the words after the sender's name. It never
    /// counts as unread, has no ticks and pushes to no one.
    /// </summary>
    public const string GroupEvent = "group_event";
}
