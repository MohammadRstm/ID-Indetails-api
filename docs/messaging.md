# Messaging

Team chats and direct messages, as in the prototype (`75-messages-data.js`, `75-messages-page.js`). Chats are addressed by team or by person, never by an internal id, and a conversation is only stored once its first message is sent.

## Endpoints

All of them need only a signed-in, active user. Who may read and write each chat follows role and team (below), not permissions.

| Method and path | Purpose |
|---|---|
| `GET /api/messages/conversations` | Every team chat the caller is in (with or without messages) and their direct chats with messages, most recent first. Each has the team or the person, the last message, the unread count and, for team chats, whether an unread message mentions the caller |
| `GET /api/messages/badge` | `{ unread, mention }` for the header: every unread message, and whether any mentions the caller |
| `GET /api/messages/contacts` | The people the caller can message directly, by name |
| `GET /api/messages/teams/{teamId}/members` | Everyone in a team chat, for picking mentions |
| `GET /api/messages/teams/{teamId}?before=&limit=50` | A page of the chat, oldest first, with `hasMore`. `before` (a message id) pages back; `limit` is 1–100 |
| `POST /api/messages/teams/{teamId}` | `{ body, mentionIds }`. Returns `201` |
| `POST /api/messages/teams/{teamId}/read` | Marks the chat read up to its latest message and returns the new badge |
| `GET /api/messages/direct/{userId}?before=&limit=50` | The same for a direct chat with that person |
| `POST /api/messages/direct/{userId}` | `{ body }`. Returns `201` |
| `POST /api/messages/direct/{userId}/read` | As for team chats |

## Rules

`Business/Messaging/MessagingRules.cs`:

- **Team chats.** Every team has one. Its members are worked out live, never stored: the team's people, the admins of its domain, and the super admins. So employees see their own team's chat, admins every team chat of their domain, and super admins all of them. Someone moved to another team or domain loses the old chat, history included, and sees the new one's whole history. A chat the caller isn't in reads as `404`.
- **Direct messages.** Super admins message anyone; everyone else messages their own domain and the super admins, never the other domain. Nobody messages themselves. A person the caller can't reach reads as `404`. Only active people receive messages (`422 user_deactivated`), but a chat with someone deactivated can still be read.
- **Mentions** are only for team chats, and only of the chat's members (`400`). A mention lights up the member's badge (`mention: true`) until they read the chat.
- **Unread** means sent by someone else after the reader's read marker. Sending marks the chat read for the sender, and read markers only move forward.
- **Messages** are 1–4,000 characters and can't be edited or deleted. Attachments come with file uploads.
- **Shared comments.** A work-entry comment posted with `sendToGroup: true` also goes to the entry author's team chat as a `task_comment` message: the comment text, the entry as it is now (`taskComment`: author, project, description, start and minutes; null once the entry is deleted), and a mention of everyone in the chat, the prototype's @all. If the author is in no team, the comment is refused (`422 no_team_chat`).
- **Teams.** A team whose chat has messages can't be deleted (`422 team_has_chat`), so history is never lost. Rename it instead.
- Messages don't send bell notifications: the messages badge covers them, as in the prototype.
