# Notifications

Everyone has a bell. A notification belongs to one person and stores structured data only: its kind, who did it, the project, the subject and a payload. The client writes the text and the link from those, so web and mobile render it the same way.

## Endpoints

All of them need only a signed-in user and act on the caller's own notifications.

| Method and path | Purpose |
|---|---|
| `GET /api/notifications?unreadOnly=&page=&pageSize=` | Newest first, 20 a page by default (1–100) |
| `GET /api/notifications/unread-count` | `{ unread }`, for the badge. Reload it on the live `notifications` signal (see [messaging.md](messaging.md#live-updates)) |
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

Reading checks again. A notification sent because of what the reader could see (someone's hours, money, salaries) is only listed, counted and openable while they still hold that permission: `work_logged` and `work_digest` need `work_entry:view_all`, `hours_short` needs `salary:view`, and the money kinds need `financial:view` (`Business/Notifications/NotificationAccess.cs`).

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
| `portal_client_message` | Staff taking part in a project's client portal discussion, when a client writes in its chat. Rolling per project (`portal_chat:{projectId}`) | — | `projectTitle`, `authorName` (never the text) |
| `portal_client_comment` | The project's manager and the file's uploader (everyone taking part on the staff side when neither does), when a client comments on a file. Rolling per file (`portal_comments:{itemId}`) | the item | `projectTitle`, `authorName`, `itemTitle` |
| `portal_mention` | Staff mentioned in a client portal message or comment, once each (`portal_mention:m{id}` / `c{id}`) | `message` or `comment` (subject id: the item, for comments) | `projectTitle`, `authorName`, `itemTitle` |
| `project_file_comment` | The file's uploader and everyone who commented on it before, never the author, when someone comments on a project file. Rolling per file (`file_comments:{itemId}`, counting again from 1 once read). Push: Messages, "File comment" | the item | `projectTitle`, `fileTitle`, `authorName` (never the text) |
| `project_closed` | Holders of `financial:view`, when a client project with revenue is finalised (completed, or archived without being completed). Each finalisation sends it: a project reopened and completed again reports its new margin | the project | `projectTitle`, `marginPct` (the final margin as a percentage, one decimal; never an amount) |

## Scheduled notifications

Nobody's action sends these, so they have no actor. A background timer (`Jobs/NotificationScheduler`) runs every check when the API starts and then every `NotificationScheduler:IntervalMinutes` (60 by default); `NotificationScheduler:Enabled` switches it off, as the integration tests do. Each check runs on its own, so one that fails is logged and retried on the next tick without stopping the others.

Every scheduled notification carries a key (`digest_key`) and goes to a person only once per key, however often the checks run. The unique recipient and key index stops a second API instance from sending it twice.

| Kind | When | Sent to | Subject | Payload |
|---|---|---|---|---|
| `weekly_summary` | From the first day of the week (the company's week start), for the week before, while Design's `weeklySummary` is on. Key `weekly-summary-{weekStart}` | Each Design employee holding `work_entry:create` (admins log no hours), zero hours included | — | `weekStart`, `minutes`, `entries`, `projects` |
| `project_idle` | An active Design project with no work for 7 days (counted from its start date when it has none), while Design's `notifyIdle` is on. Again after each new quiet stretch: key `project-idle-{projectId}-{quietSince}` | Its owner and holders of `project:change_status` | the project | `projectTitle`, `days`, `lastWorkOn` |
| `requirement_due_soon` | The day before an open requirement of an active project is due. Key `requirement-due-{id}-{deadline}` | Its assignees | the requirement | `projectTitle`, `text`, `deadline` |
| `requirement_overdue` | Once its deadline has passed, for deadlines up to 7 days ago (older ones are left alone, so turning this on doesn't flood anyone). Key `requirement-overdue-{id}-{deadline}` | Its assignees and holders of `requirement:manage` | the requirement | `projectTitle`, `text`, `deadline` |
| `invoice_overdue` | An issued invoice still unpaid after its due date. Key `invoice-overdue-{id}` | Holders of `financial:view` in its domain | the invoice | `number`, `dueOn` |
| `hours_short` | From the first of a month, when Design employees finished the month before flagged in the hours ledger. Key `hours-short-YYYY-MM` | Holders of `salary:view` | — | `month`, `count`, `people` (the 5 most short: `id`, `name`, `shortMinutes`) |
| `project_cost_high` | An active Design client project's cost has passed 85% of its payment (its revenue). Not for Execution, whose payment includes its materials at client price. Once per project: key `project-cost-{projectId}` | Holders of `financial:view` in its domain | the project | `projectTitle`, `usedPct` (the cost as a whole percentage of the payment) |
| `invoice_draft_waiting` | A draft invoice still unissued 3 days after it was created. Key `invoice-draft-{id}` | Holders of `financial:view` in its domain | the invoice | `projectTitle`, `createdOn` |
| `invoice_sent_automatically` | A scheduled invoice went out on its day ([billing.md](billing.md), "Auto invoice"). Each one, no key | Whoever scheduled it | the invoice | `number`, `to` (null when what was on account paid it, so it wasn't emailed) |
| `invoice_send_due` | A scheduled invoice with no address to email reached its day, so it isn't issued. Key `invoice-send-due:{id}:{sendOn}` | Whoever scheduled it, to send it themselves | the invoice | `sendOn` |
| `invoice_send_failed` | A scheduled invoice's email failed; it is tried again on every run. Key `invoice-send-failed:{id}:{sendOn}` | Whoever scheduled it | the invoice | `sendTo` |
| `invoice_reminder_due` | An overdue invoice is due a reminder, but there is no address to email it to. Key `invoice-reminder-due:{id}` | Holders of `invoice:send` in its domain, to chase it themselves | the invoice | `number` |

The same timer also runs the domains' auto-archive (see [projects.md](projects.md), "Status"). Percentages (`usedPct`, `marginPct`) are money information, so they only go to holders of `financial:view`, and no notification carries an amount (N5).

A new deadline gets a new key, so moving a requirement's deadline warns again. Paying an invoice stops it being overdue; one that becomes overdue again later isn't flagged twice.

## Pushes

The phone app gets pushes for every notification and every chat message, and two reminders of its own. The bell and the in-app list are unchanged.

### How a push goes out

1. **Staged.** `NotificationSender` (all three send methods), the work digest of `WorkLogNotifier`, and `MessagingService` (team, direct and task-comment messages) stage pushes in `IPushQueue`, next to the live signals.
2. **Queued once committed.** `LiveUpdatesMiddleware` hands a request's pushes to an in-process queue only once it has succeeded; a failed request queues none. The scheduled checks and the invoice jobs queue theirs after their work.
3. **Checked again and sent.** `Jobs/PushDispatcher` decides everything at send time (`Services/Push/Implementation/PushDelivery`):
   - only phones whose session still has a valid refresh token receive anything (devices are tied to the phone app's session, see [me.md](me.md#my-devices));
   - a deactivated person gets nothing;
   - a notification goes out only while its reader may still read it: the same check as the list (the domains they see and `NotificationAccess`), so someone who lost `financial:view` gets no money pushes. One already read isn't pushed;
   - a muted category sends nothing; during quiet hours the push arrives silently.
4. **Provider.** `ExpoPushProvider` sends through Expo's push service, 100 messages a request. It is the only part that knows Expo. Every 15 minutes `Jobs/PushReceiptsJob` checks the receipts, and a device Expo reports as `DeviceNotRegistered` (at sending or in its receipt) is deleted.

Queued pushes live in memory: one queued when the API stops is lost, but the notification row and the in-app list are not. One API instance, as for the live connection.

### What a push carries

| Part | Content |
|---|---|
| Title and body | Short text written by the server (`Business/Push/PushText.cs`). Other kinds may name the project and who acted ("Rana assigned you a requirement on Villa Haddad"). Messages show the sender and the first 100 characters |
| Money kinds | A fixed neutral text, never a client's name, an amount, a margin, a cost or pay, nor the project's title: "Payment recorded. Open ID InDetails to see it.", "An invoice needs your attention.", "An invoice couldn't be sent automatically.", "Billing update. Open ID InDetails to see it.", "A project's money needs a look.", "A new client was added.", "Pay and hours update." |
| `data` | Ids only: `{ forUserId, kind, notificationId?, target }`. `kind` is a notification kind, `message`, `log_reminder` or `labour_week_reminder`. `target` is the screen to open (`Business/Push/PushTarget.cs`): a project (with `section`), a requirement, a work entry, a chat (`teamId` or the sender's `userId`), a labour week, a person, a client, an invoice, a payment, a statement, or a page (`notifications`, `work`, ...) |
| `badge` | Unread notifications (as the bell counts them) plus unread messages |
| `channelId` | The Android channel: the category below, `reminders` for the log reminder, `quiet` during quiet hours |

A rolling digest (`work_digest`, `requirements_added`) pushes in full at most once an hour per person; the updates in between only refresh the badge (no text, no sound).

### Categories, mutes and quiet hours

| Category | Kinds |
|---|---|
| `work` | `work_logged`, `work_digest`, `entry_comment`, `weekly_summary`, `hours_excused`, `schedule_changed` |
| `requirements` | `requirement_assigned`, `requirements_added`, `requirement_done`, `requirement_due_soon`, `requirement_overdue` |
| `projects` | `project_created`, `project_status_changed`, `project_owner_set`, `project_nearly_done`, `project_idle` |
| `meetings` | `meeting_scheduled` |
| `sites` | `materials_added`, `labour_week_submitted`, `labour_week_reminder` |
| `money` | the `invoice_*` kinds, `payment_recorded`, `statement_sent`, `project_closed`, `project_cost_high`, `client_added`, `pay_changed`, `hours_short` |
| `people` | `invitation_accepted`, `role_changed`, `domain_changed`, `team_changed`, `permissions_changed` |
| `messages` | `message` (mentions and direct messages included), the client portal's `portal_client_message`, `portal_client_comment` and `portal_mention`, and `project_file_comment` |

`log_reminder` has no category, so it can't be muted. Each person chooses their own quiet hours and muted categories ([me.md](me.md#my-notification-settings)); until they do, quiet hours are 22:00 to 07:00 in the company's time zone and nothing is muted. A push during quiet hours is still delivered, without sound, on the `quiet` channel (passive on iOS); nothing waits on the server.

### Reminders

Push only: no notification row, so the website's bell is unchanged. The notification scheduler checks them every hour; each goes to a person at most once a day, remembered in memory (a restart can repeat one).

| Kind | When | To |
|---|---|---|
| `log_reminder` | In the last hour of the person's working day (their own schedule or the company week), when what they logged within their hours is less than the day requires. Not on a day off or an excused day. While Design's `logReminder` is on | Design people who log hours (`work_entry:create`) |
| `labour_week_reminder` | Saturday from 15:00 company time, one per site whose current week isn't reported | Execution people with `labour_report:create`, for each active site they added a material or submitted a week on in the last 4 weeks |

The timing rules are in `Business/Push/ReminderRules.cs`.

### Configuration

| Setting | Default | Notes |
|---|---|---|
| `Push:Enabled` | `false` | Nothing is queued or sent while off. `PUSH_ENABLED` in the server's `.env` |
| `Push:Expo:AccessToken` | empty | **Secret.** Expo's access token ("enhanced push security"). User secrets locally, `EXPO_ACCESS_TOKEN` in the server's `.env`. Without it, pushes are sent unauthenticated |
| `Push:DeviceCleanupEnabled` | `true` | The daily removal of devices whose session ended. Integration tests switch it off |
