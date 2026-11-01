using System.ComponentModel;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Messaging.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record FindRecipientInput
{
    [Description("Part of the person's or team's name, as the user said it.")]
    public required string Text { get; init; }
}

/// <param name="Kind">person or team.</param>
/// <param name="Detail">What tells people of the same name apart: role, position, team and business.</param>
public sealed record RecipientRow(string Kind, long Id, string Name, string Detail);

/// <summary>
/// Who the user may message, by name: the people their chat lists as contacts and the team chats they are in, read
/// as the user through the messaging service. Not the employee directory: someone listed there may be out of reach.
/// </summary>
public sealed class FindRecipientTool(IMessagingService messaging) : ProtonTool<FindRecipientInput>
{
    private const int Max = 8;

    public override string Name => "find_recipient";

    public override string Description =>
        "Find someone the user can message, or a team chat they are in, by name, before opening a chat or drafting a " +
        "message. Several matches: let the user pick with offer_choices.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<MessagesController>(nameof(MessagesController.Contacts));

    protected override async Task<ProtonToolOutput> RunAsync(FindRecipientInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var text = input.Text.Trim();
        if (text.Length < 2)
        {
            throw new ProtonToolInputException("Give at least two letters of the name.");
        }

        bool Matches(string name) => name.Contains(text, StringComparison.OrdinalIgnoreCase);
        var people = (await messaging.ContactsAsync(cancellationToken))
            .Where(c => Matches(c.Name))
            .Select(c => new RecipientRow("person", c.Id, c.Name,
                string.Join(", ", new[] { c.Role.Replace('_', ' '), c.Position, c.Team?.Name, c.Domain }.Where(s => !string.IsNullOrWhiteSpace(s)))));
        var teams = (await messaging.ConversationsAsync(cancellationToken))
            .Select(c => c.Team)
            .OfType<DTO.Response.Messaging.ChatTeamResponse>()
            .Where(t => Matches(t.Name))
            .Select(t => new RecipientRow("team", t.Id, t.Name, $"team chat, {t.Domain}"));
        var rows = people.Concat(teams)
            .OrderBy(r => r.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(Max)
            .ToList();
        return new ProtonToolOutput(rows, rows.Select(r => new ProtonRef(r.Kind == "team" ? ProtonRefType.Team : ProtonRefType.Person, r.Id, r.Name)).ToList());
    }
}

public sealed record InboxInput;

/// <param name="LastActivity">When the last message came, company time; never what it said.</param>
public sealed record InboxRow(string Kind, long Id, string Name, int Unread, bool MentionsYou, string? LastActivity);

/// <summary>The user's chats as the header lists them: who, how many unread, a mention, when. No message is read.</summary>
public sealed class GetMyInboxTool(IMessagingService messaging, IClock clock) : ProtonTool<InboxInput>
{
    public override string Name => "get_my_inbox";

    public override string Description =>
        "The user's own chats: who or which team, unread messages, whether one mentions them, and when the last came. " +
        "You can't read what messages say; open the chat for the user instead.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<MessagesController>(nameof(MessagesController.Conversations));

    protected override async Task<ProtonToolOutput> RunAsync(InboxInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var rows = (await messaging.ConversationsAsync(cancellationToken))
            .Select(c => c.Team is { } team
                ? new InboxRow("team", team.Id, team.Name, c.Unread, c.UnreadMention, clock.Local(c.LastMessage?.CreatedAt))
                : c.Person is { } person
                    ? new InboxRow("person", person.Id, person.Name, c.Unread, c.UnreadMention, clock.Local(c.LastMessage?.CreatedAt))
                    : new InboxRow("announcements", 0, "Announcements", c.Unread, false, clock.Local(c.LastMessage?.CreatedAt)))
            .OrderByDescending(r => r.Unread > 0)
            .ThenByDescending(r => r.LastActivity)
            .Take(20)
            .ToList();
        // Announcements is no record Proton can open
        return new ProtonToolOutput(rows, rows
            .Where(r => r.Kind != "announcements")
            .Select(r => new ProtonRef(r.Kind == "team" ? ProtonRefType.Team : ProtonRefType.Person, r.Id, r.Name))
            .ToList());
    }
}

public sealed record NotificationsInput
{
    [Description("Only the unread ones. Default true.")]
    public bool? UnreadOnly { get; init; }
}

/// <param name="What">The kind of notification in words ("requirement assigned").</param>
public sealed record NotificationRow(string What, string? From, ProtonNamed? Project, int Count, string When, bool Read);

/// <summary>The user's own notifications, newest first: what happened, who did it, and on which project.</summary>
public sealed class GetMyNotificationsTool(INotificationService notifications, IClock clock) : ProtonTool<NotificationsInput>
{
    public override string Name => "get_my_notifications";

    public override string Description => "The user's own notifications, newest first: what happened, who did it, on which project, and when.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<NotificationsController>(nameof(NotificationsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(NotificationsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var page = await notifications.ListAsync(new NotificationListQuery { UnreadOnly = input.UnreadOnly ?? true, PageSize = 15 }, cancellationToken);
        var rows = page.Items
            .Select(n => new NotificationRow(
                n.Kind.Replace('_', ' ').Replace('.', ' '),
                n.Actor?.Name,
                n.Project is { } p ? new ProtonNamed(p.Id, p.Title) : null,
                n.Count,
                clock.Local(n.CreatedAt),
                n.ReadAt is not null))
            .ToList();
        var refs = page.Items.Where(n => n.Project is not null)
            .Select(n => new ProtonRef(ProtonRefType.Project, n.Project!.Id, n.Project.Title, n.Project.Domain))
            .DistinctBy(r => r.Id)
            .ToList();
        return new ProtonToolOutput(rows, refs);
    }
}
