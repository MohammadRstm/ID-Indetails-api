# Messaging

Team chats and direct messages, as in the prototype (`75-messages-data.js`, `75-messages-page.js`), the Announcements chat everyone reads (28 Oct 2026), and custom groups a super admin makes of whoever they pick (24 Nov 2026). Team and direct chats are addressed by team or by person, never by an internal id, and are only stored once their first message is sent; Announcements was created by its migration. A custom group is stored when it is made, and addressed by its id.

## Endpoints

All of them need only a signed-in, active user, except writing in Announcements (`message:announce`) and making and changing custom groups (`message:manage_groups`). Who may read and write team and direct chats follows role and team (below), not permissions; a custom group is read and written by its members.

| Method and path | Purpose |
|---|---|
| `GET /api/messages/conversations` | Announcements first (`type: announcements`, no team or person, even before anyone wrote in it), then every team chat and custom group the caller is in (with or without messages) and their direct chats with messages, most recent first. Each has the team, the person or the `group` (`{ id, name, photoFileId, memberCount }`, for `type: custom_group`), the last message (with its `receipt`, if the caller sees it), the unread count and, for team chats and custom groups, whether an unread message mentions the caller and `onlineCount`: how many of the others in it have the app open. A person carries `online` and `lastSeenAt` |
| `GET /api/messages/badge` | `{ unread, mention }` for the header: every unread message, and whether any mentions the caller. It counts as the inbox does, with fewer queries: no last messages, and no looking for mentions or people when nothing is unread. Loading it means the caller's device has their messages, so it moves their received markers ([ticks](#ticks-and-seen-by)) |
| `GET /api/messages/contacts` | The people the caller can message directly, by name, each with `online` and `lastSeenAt` |
| `GET /api/messages/teams/{teamId}/members` | Everyone in a team chat, for picking mentions |
| `GET /api/messages/teams/{teamId}?before=&limit=50` | A page of the chat, oldest first, with `hasMore`. `before` (a message id) pages back; `limit` is 1–100 |
| `POST /api/messages/teams/{teamId}` | `{ body, mentionIds }`. Returns `201` |
| `POST /api/messages/teams/{teamId}/read` | Marks the chat read up to its latest message and returns the new badge |
| `GET /api/messages/direct/{userId}?before=&limit=50` | The same for a direct chat with that person |
| `POST /api/messages/direct/{userId}` | `{ body }`. Returns `201` |
| `POST /api/messages/direct/{userId}/read` | As for team chats |
| `GET /api/messages/announcements?before=&limit=50` | A page of Announcements, as for team chats |
| `POST /api/messages/announcements` | `message:announce`: `{ body, attachmentIds }`, no mentions (`400`). Returns `201` |
| `POST /api/messages/announcements/read` | As for team chats |
| `GET /api/messages/groups/{groupId}/members` | A custom group's active members, for its members ([Custom groups](#custom-groups)) |
| `GET /api/messages/groups/{groupId}?before=&limit=50` | A page of a custom group the caller is in, as for team chats |
| `POST /api/messages/groups/{groupId}` | `{ body, attachmentIds, mentionIds }` (mentions of its members). Returns `201` |
| `POST /api/messages/groups/{groupId}/read` | As for team chats |
| `POST /api/messages/groups/{groupId}/leave` | The caller leaves a group they are in. Returns `204` |
| `GET /api/messages/groups` | `message:manage_groups`: every custom group with its active `members` and whether the caller `isMember` |
| `GET /api/messages/groups/people` | `message:manage_groups`: every active member of staff, of any business, who can be put in a group |
| `POST /api/messages/groups` | `message:manage_groups`: `{ name, memberIds }`. The caller is put in it too. Returns `201` with the group |
| `PUT /api/messages/groups/{groupId}` | `message:manage_groups`: `{ name }` renames it |
| `POST /api/messages/groups/{groupId}/members` | `message:manage_groups`: `{ userIds }` adds people |
| `DELETE /api/messages/groups/{groupId}/members/{userId}` | `message:manage_groups`: takes someone out |
| `PUT /api/messages/groups/{groupId}/photo`, `DELETE …/photo` | `message:manage_groups`: `{ fileId }`, a photo from the caller's upload, or back to the people icon |
| `DELETE /api/messages/groups/{groupId}` | `message:manage_groups`: deletes the group with its whole history. Returns `204` |
| `DELETE /api/messages/{messageId}` | Deletes the caller's own message for everyone, with its files ([below](#deleting-a-message)). Returns `204` |
| `GET /api/messages/{messageId}/receipts` | Who has seen the message (`seen`, each with `at` where known), who has received it without reading it (`delivered`) and who hasn't (`notReceived`), with the `summary`. For the people who see receipts ([below](#ticks-and-seen-by)): `403` for anyone else in the chat, `404` for a message they can't read |

### Ticks and Seen by

Decided 20 November 2026 (`Business/Messaging/MessageReceipts.cs`):

- **Ticks.** Each message carries `receipt: { status, seen, delivered, of }` for those who see it: `sent` (one grey tick), `delivered` once everyone it is for has received it (two grey), `read` once all of them have read it (two blue). `of` is how many it is for, `seen` how many have read it, `delivered` how many have received it, readers included. A message for nobody (a team chat of one) stays `sent`.
- **Who it is for.** Everyone in the chat now but its sender: the other person in a direct chat, the team chat's or custom group's active members, every active member of staff in Announcements, but there only those who had joined when it was sent. A custom group's grey lines have no ticks.
- **Received** means one of the person's devices has their messages: loading the badge (`GET /api/messages/badge`, which the app does whenever a message arrives while it is open) moves their `conversation_reads.last_delivered_message_id` up to the newest message others sent in each chat with something unread. Reading counts as receiving. Proton reading messages for someone moves nothing.
- **Read** is the read marker, which the app moves only while the chat is on screen.
- **Who sees receipts.** A message's sender, for their own messages, in every chat. In team chats the super admins see them for every message too, and in custom groups the super admins in the group. In Announcements, whoever may announce (`message:announce`). Everyone else gets `receipt: null`.
- **When they read it.** Announcements record when each person read each one (`message_reads`, from 20 November 2026 on; what was read before has no time). In the other chats the read marker keeps only when it last moved, so `at` is given only when it is the last message that person read.
- **Live.** When someone receives or reads messages, their senders get the `receipts` signal for that chat (as they see it), and in team chats and Announcements so do the super admins, in custom groups those in it.

### Last seen

Contacts, team members and the people of direct chats carry `online` (the app open now, [below](#online-and-last-seen)) and `lastSeenAt`, for everyone who can message them. The app shows "Online", or "Last seen …", in the list of chats and at the top of a direct chat, and how many are online in a team chat.

## Rules

`Business/Messaging/MessagingRules.cs`:

- **Team chats.** Every team has one. Its members are worked out live, never stored: the team's people, the admins of its domain, and the super admins. So employees see their own team's chat, admins every team chat of their domain, and super admins all of them. Someone moved to another team or domain loses the old chat, history included, and sees the new one's whole history. A chat the caller isn't in reads as `404`.
- **Direct messages.** Super admins message anyone; everyone else messages their own domain and the super admins, never the other domain. Nobody messages themselves. A person the caller can't reach reads as `404`. Only active people receive messages (`422 user_deactivated`), but a chat with someone deactivated can still be read.
- **Announcements.** One chat that every active member of staff reads: every role and domain, the website's staff included (portal clients aren't staff). Only holders of `message:announce`, the super admins by default, write in it (`403` otherwise). A new message pushes to everyone else's phone, titled "Announcements" (category `messages`), and counts in their badge; there is no bell notification. What was announced before someone joined (`users.created_at`) never counts as unread for them, though they can read it.
- **Mentions** are only for team chats and custom groups, and only of the chat's members (`400`). A mention lights up the member's badge (`mention: true`) until they read the chat.
- **Unread** means sent by someone else after the reader's read marker. Sending marks the chat read for the sender, and read markers only move forward.
- **Messages** are up to 4,000 characters and can't be edited. Their sender can delete them ([below](#deleting-a-message)). A message has text, photos, videos or a voice note, or any mix: `attachmentIds` takes up to 10 of the sender's own uploads, each attached once, which the chat's people can then download ([files.md](files.md)). Each message lists its `attachments`.
- **One voice note per message.** At most one of the attachments is an audio file, from `POST /api/files/voice` (`422 too_many_voice_notes`, "A message can carry one voice note."). It works the same in team chats, direct messages and Announcements; the recording is sent as the message's only attachment, with no text, and the attachment's `kind` is `audio`.
- **Shared comments.** A work-entry comment posted with `sendToGroup: true` also goes to the entry author's team chat as a `task_comment` message: the comment text, the entry as it is now (`taskComment`: author, project, description and start; null once the entry is deleted). Its `minutes` and `attachment` are only filled for readers who may read the entry itself (its author and holders of `work_entry:view_all`): colleagues' hours stay private, and a mention of everyone in the chat, the prototype's @all. If the author is in no team, the comment is refused (`422 no_team_chat`).
- **Teams.** A team whose chat has messages can't be deleted (`422 team_has_chat`), so history is never lost. Rename it instead.
- Messages don't send bell notifications: the messages badge covers them, as in the prototype. They do push to the other people's phones, with the sender and a short preview ([notifications.md](notifications.md#pushes)): a message with no text shows "Sent a photo or video.", or "Sent a voice message." when its only attachment is a voice note.
- **Sent through Proton.** A message Proton sent as the user, after they confirmed its card, carries
  `messages.proton_action_id` (the confirmed action; null for everything typed, and set back to null if that action is
  ever cleared out). Each message says so as `viaProton`, and the chat shows a small "with Proton" mark, so recipients
  know. See [proton.md](proton.md#messages-sent-as-the-user-stage-5-phase-7).

## Deleting a message

Decided 24 November 2026 for voice messages, and widened the same day to every message (`MessagingRules.CheckDelete`, `MessagingService.DeleteAsync`):

- **Only the sender**, at any time, in a team chat, a direct chat or Announcements: anyone else in the chat gets `403`, and a message the caller can't read `404`. Text, photos, videos and voice notes can all be deleted. A shared work-entry comment can't (`422 message_not_deletable`, "A shared work comment can't be deleted from the chat."): it belongs to the comment.
- **Nothing is left.** The message goes for everyone, with no "deleted" placeholder, and so do its attachments' files: their rows in the same transaction and then the files on disk (`IFileStorage.Delete`). The chat's last message and unread counts simply fall back to the message before.
- **Read and received markers** that stood on the message move back to the chat's message before it, and in Announcements the times people read it are dropped.
- **Live.** Everyone in the chat gets the `messages` signal and reloads it. A push already delivered to a phone can't be called back; one still waiting is dropped, as its message is gone.
- Proton doesn't delete messages: it opens the chat, where the sender deletes it from the message's menu.

## Custom groups

Decided by the owner 24 November 2026 (`Business/Messaging/MessagingRules.cs`, `GroupEvents.cs`, `MessagingService`):

- **What it is.** A named chat of whoever a holder of `message:manage_groups` (the super admins by default) picked: any active member of staff, from any business, the website's staff included. Portal clients aren't staff and can't be added. Members are stored (`conversation_members`), unlike a team chat's. At most 200 people, a name of up to 80 characters.
- **Who is in it.** Exactly its members. Whoever makes a group is put in it, and can leave it like anyone else. A super admin is in a group only when added: one who isn't still sees it in the managing list (`GET /api/messages/groups`, with `isMember: false`) and can change or delete it, but can't read it (`404`), as for anyone outside it. Someone deactivated stays a member but takes no part (not counted, not told) until they are back.
- **Managing** (`message:manage_groups`, any holder, not only the one who made it): rename it, change its photo, add people, take people out, delete it. A group the caller can't find is `404`; without the permission, `403`.
- **Added people** see the group's whole history, none of it unread: their read marker starts at the line saying they were added.
- **Taken out, or leaving**, someone loses the chat entirely, history included, as when they leave a team. Any member may leave (`POST …/leave`, "Leave group" in the chat's menu).
- **Grey lines.** Each change leaves a line in the chat (`kind: group_event`), from whoever made it, whose `body` is the words after their name: "created the group “Site leads”", "renamed the group to “…”", "added Sara Haddad and Ali Nasser", "removed Sara Haddad", "left the group", "changed the group photo", "removed the group photo". Names stay as they were then. A line never counts as unread, has no ticks, pushes to no one and can't be deleted.
- **The photo.** A photo from the manager's upload (`FileKind.Image`), shown instead of the people icon. Its members and whoever manages groups can download it ([files.md](files.md)). A photo replaced or removed is attached to nothing, so the upload cleanup deletes it.
- **Deleting** takes everything in it for everyone: its messages (their mentions and attachments go with them), read markers and read times, members, and the files of its attachments and photo (the rows in the same transaction, then the files on disk). The app asks first, saying the whole history goes for everyone.
- **Messages** in a group work as in a team chat: text, photos, videos and voice notes, mentions of its members, ticks and "Seen by" for the sender and the super admins in it, and a push to everyone else in it, titled "{sender} in {group}" (`PushTarget` `conversation` with `groupId`; a phone app that doesn't know it opens the messages).
- **Proton** sends to a group and marks it read like a team chat (`propose_send_message` and `propose_mark_chat_read` with `group_id`), and `find_recipient` and `get_my_inbox` list the user's groups. It can't open a group's chat or draft into it: a group is no record it links to yet. Making and changing groups, and leaving one, it leaves to the person on the messages page ([proton.md](proton.md)).

## Live updates

A SignalR connection at `/hubs/live` tells signed-in clients that something changed, so they reload it instead of polling. It carries no content.

| Signal | Payload | Sent to | Reload |
|---|---|---|---|
| `notifications` | — | Whoever got a notification, or read theirs (for their other devices) | `GET /api/notifications/unread-count`, and the open list |
| `messages` | `{ teamId, userId, announcements, groupId }`: one of `teamId`, `userId` and `groupId` set, or `announcements: true` | Everyone in a team chat or custom group that got a message or lost a deleted message (the same for direct chats and Announcements); everyone in a custom group, whoever changed it and whoever was taken out, when it is made, renamed, deleted or its photo or people change; both sides of a direct message; every active person when Announcements gets one; whoever read a chat (for their other devices) | `GET /api/messages/badge`, the conversation list, and that chat if it's open |
| `receipts` | As for `messages` | The senders of messages someone just received or read, and in team chats and Announcements the super admins | That chat if it's open, its open "Seen by", and the conversation list |

- **Connecting.** It needs a signed-in user. Browsers pass the access token as the `access_token` query parameter, because WebSockets can't send headers; that parameter is only read on `/hubs/live`, and request logging stays at Warning so query strings aren't logged. The React client uses `@microsoft/signalr` with `accessTokenFactory`.
- **When.** Signals are staged while a request runs and sent only once it has succeeded (`Middlewares/LiveUpdatesMiddleware`), so a refused or failed request signals nobody. The scheduled notification checks send theirs after each check. A signal that can't be sent is logged and dropped; the request still succeeds.
- **Scale.** One API instance for now. Several would need a SignalR backplane such as Redis.

### Online and last seen

- **Online** means having the live connection open (`Services/Realtime/Presence`); two tabs keep someone online until both close. `GET /api/presence` (any signed-in user) returns `{ online: [userIds] }` for the people of the domains the caller can see, super admins included; the client reloads it for the dots on avatars and the "Online now" count. Employee responses carry `online`, and the Employees page rows (`/api/work/people`) carry `online` and `lastSeenAt`.
- **Last seen** (`users.last_seen_at`) moves with use: any signed-in request records it, at most every 5 minutes per person (`Middlewares/LastSeenMiddleware`), and closing the last live connection records it too. Sign-in and token refresh still set it.
- Presence lives in the API's memory, so it is per instance, like the live connections.
