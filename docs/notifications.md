# Notifications

Everyone has a bell. A notification belongs to one person and stores structured data only: its kind, who did it, the project, the subject and a payload. The client writes the text and the link from those, so web and mobile render it the same way.

## Endpoints

All of them need only a signed-in user and act on the caller's own notifications.

| Method and path | Purpose |
|---|---|
| `GET /api/notifications?unreadOnly=&page=&pageSize=` | Newest first, 20 a page by default (1–100) |
| `GET /api/notifications/unread-count` | `{ unread }`, for the badge. The client polls it until live updates arrive with messaging |
| `POST /api/notifications/{id}/read` | Marks one read and returns the unread count left |
| `POST /api/notifications/read-all` | Marks all read and returns `{ unread: 0 }` |

- Someone else's notification reads as `404`.
- A notification about a project of a domain the person can no longer see (they moved, or lost a cross-domain grant) is hidden from the list and the count, and reads as `404`.
- Each item has `kind`, `actor` (`id`, `name`), `project` (`id`, `title`, `domain`), `subjectType`, `subjectId`, `payload`, `count` (how many things a rolling digest sums up, otherwise 1), `createdAt` and `readAt`.

## Who receives one

Every notification goes through one sender (`INotificationSender`), which applies the same rules everywhere:

- never the person who did it;
- each person once;
- only active people who can see the notification's domain;
- no money in payloads, so a notification can't reveal amounts to someone who later loses `financial:view`. The page it links to checks permissions as usual.

"Holders of a permission" means the active people who hold it and can see the domain, super admins and people granted it from the other domain included.

## Kinds

| Kind | Sent to | Subject | Payload |
|---|---|---|---|
| `requirement_assigned` | People newly assigned a requirement | the requirement | `text`, `deadline`, `projectTitle` |
| `work_logged` | Holders of `work_entry:view_all` in Design, per `notifyEntry` (see [design.md](design.md)) | the work entry | `authorName`, `projectTitle`, `minutes`, `description` |
| `work_digest` | The same, when `notifyEntry` is `digest`: one per person per day | — | `minutes`, `latestName`, `projectTitle` |
| `entry_comment` | The work entry's author | the work entry | `authorName`, `text`, `entryDescription` |
| `meeting_scheduled` | Holders of `meeting:manage`, when someone without it sets a meeting | the meeting | `authorName`, `projectTitle`, `topic`, `startsAt`, `endsAt` |
| `materials_added` | Holders of `material:manage` | the project | `count`, `projectTitle` |
| `labour_week_submitted` | Holders of `labour_report:manage` | the project | `weekStart`, `updated`, `projectTitle` |
