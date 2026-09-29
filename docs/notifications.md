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

"Holders of a permission" means the active people who hold it and can see the domain, super admins and people granted it from the other domain included. "The domain's people" means the active admins and employees whose home domain it is; super admins are left out of these broadcasts.

Some notifications are **rolling digests**: one per person per digest key. Each repeat adds 1 to `count`, takes the newest payload and actor, and shows as new and unread again.

Project news (new projects, status changes, added requirements) follows the domain's `notifyProject` setting. Everything else always sends.

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
| `project_created` | The domain's people | the project | `projectTitle` |
| `project_status_changed` | The domain's people, on hold, resume, complete and reopen (not archive) | the project | `projectTitle`, `change` (`hold`, `resume`, `complete`, `reopen`) |
| `project_owner_set` | The new owner | the project | `projectTitle` |
| `project_nearly_done` | The owner and holders of `project:change_status`, each time an active project's progress crosses 90% | the project | `projectTitle`, `progressPct` |
| `requirements_added` | The domain's people other than the requirement's assignees. Rolling: one per project per day (`requirements-added-{projectId}-YYYY-MM-DD`) | the latest requirement | `projectTitle`, `latestText` |
| `requirement_done` | Its assignees and holders of `requirement:manage` | the requirement | `projectTitle`, `text` |
| `invitation_accepted` | Whoever sent the invitation, and holders of `employee:invite` in the new person's domain | the new person | `role`, `domain` |
| `role_changed` | The person, after a manual change or an automatic one from a permission change | the person | `from`, `to`, `domain`, `automatic` |
| `domain_changed` | The person moved | the person | `from`, `to` |
| `team_changed` | The person, when an admin changes their team | the person | `teamId` (null when taken out of a team) |
| `pay_changed` | The person | the person | `effectiveFrom` (never the amount) |
| `permissions_changed` | The person | the person | `granted`, `revoked`, `reset` (permission codes) |
| `hours_excused` | The person | the excuse | `month`, `kind`, `minutes`, `days`, `isPaid` |
| `schedule_changed` | The person | the person | `effectiveFrom`, `followsDefault` |
| `client_added` | Holders of `client:manage` in the client's domain | the client | `name`, `organisation` |
| `invoice_issued` | Holders of `financial:view` | the invoice | `number` |
| `payment_recorded` | Holders of `financial:view` | the payment | `invoiceNumber`, `isInitial` |
| `statement_sent` | Holders of `financial:view` | the statement | `number` |

## Scheduled notifications

Nobody's action sends these, so they have no actor. A background timer (`Jobs/NotificationScheduler`) runs every check when the API starts and then every `NotificationScheduler:IntervalMinutes` (60 by default); `NotificationScheduler:Enabled` switches it off, as the integration tests do. Each check runs on its own, so one that fails is logged and retried on the next tick without stopping the others.

Every scheduled notification carries a key (`digest_key`) and goes to a person only once per key, however often the checks run. The unique recipient and key index stops a second API instance from sending it twice.

| Kind | When | Sent to | Subject | Payload |
|---|---|---|---|---|
| `weekly_summary` | From the first day of the week (the company's week start), for the week before, while Design's `weeklySummary` is on. Key `weekly-summary-{weekStart}` | Each Design admin and employee holding `work_entry:create`, zero hours included | — | `weekStart`, `minutes`, `entries`, `projects` |
| `project_idle` | An active Design project with no work for 7 days (counted from its start date when it has none), while Design's `notifyIdle` is on. Again after each new quiet stretch: key `project-idle-{projectId}-{quietSince}` | Its owner and holders of `project:change_status` | the project | `projectTitle`, `days`, `lastWorkOn` |
| `requirement_due_soon` | The day before an open requirement of an active project is due. Key `requirement-due-{id}-{deadline}` | Its assignees | the requirement | `projectTitle`, `text`, `deadline` |
| `requirement_overdue` | Once its deadline has passed, for deadlines up to 7 days ago (older ones are left alone, so turning this on doesn't flood anyone). Key `requirement-overdue-{id}-{deadline}` | Its assignees and holders of `requirement:manage` | the requirement | `projectTitle`, `text`, `deadline` |
| `invoice_overdue` | An issued invoice still unpaid after its due date. Key `invoice-overdue-{id}` | Holders of `financial:view` in its domain | the invoice | `number`, `dueOn` |
| `hours_short` | From the first of a month, when Design employees finished the month before flagged in the hours ledger. Key `hours-short-YYYY-MM` | Holders of `salary:view` | — | `month`, `count`, `people` (the 5 most short: `id`, `name`, `shortMinutes`) |

A new deadline gets a new key, so moving a requirement's deadline warns again. Paying an invoice stops it being overdue; one that becomes overdue again later isn't flagged twice.
