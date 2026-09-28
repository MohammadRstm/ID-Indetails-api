# Design work

Features of the Design domain: domain settings, work entries, comments, meetings, and the work views and reports built on them. A record of a domain the caller can't see reads as `404`, for every endpoint below.

## Domain settings

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/domain-settings/{domain}` | Signed in, able to see the domain | The domain's time-tracking and notification rules. Employees read them too |
| `PUT /api/domain-settings/{domain}` | `domain_settings:manage` | Replaces every setting |

- No rows are seeded. Until a domain's admins first save, `GET` returns the defaults with `updatedAt: null`.
- Every save writes `domain_settings_updated`.

| Setting | Default | Values | Effect |
|---|---|---|---|
| `editWindow` | `one_day` | `none`, `one_day`, `seven_days`, `unlimited` | How long after it was logged an entry may be edited, deleted or restored |
| `allowOverlap` | `false` | | Whether a person's entries may overlap |
| `allowFutureEntries` | `false` | | Whether an entry may end after now |
| `requireDescription` | `true` | | Whether an entry needs a description |
| `timerEnabled` | `true` | | Shown by the client (the timer runs in the client) |
| `logReminder` | `true` | | Shown by the client (the afternoon reminder) |
| `notifyEntry` | `all` | `all`, `long` (3 hours or more), `digest`, `off` | Notifications to admins when work is logged |
| `notifyProject`, `notifyIdle`, `weeklySummary`, `autoArchive` | `true`, `true`, `true`, `false` | | Stored only for now; the jobs that use them come later |
| `holdLogging` | `false` | | Whether work may still be logged on a project on hold |

## Work entries

| Method and path | Needs | Purpose |
|---|---|---|
| `POST /api/work-entries` | `work_entry:create` | Logs the caller's work: `{ projectId, description, startedAt, endedAt, moves }`. Returns `201` |
| `PUT /api/work-entries/{id}` | `work_entry:create` | Replaces project, description and times of one of the caller's entries |
| `DELETE /api/work-entries/{id}` | `work_entry:create` | Soft delete (`deleted_at`). Returns `204` |
| `POST /api/work-entries/{id}/restore` | `work_entry:create` | Undoes a delete |
| `GET /api/work-entries?from=&to=&projectId=&userId=&clientId=&search=&page=1&pageSize=25` | `work_entry:view_all` | Everyone's Design entries, newest first |
| `GET /api/me/work-entries?from=&to=&projectId=&search=&page=1&pageSize=25` | `work_entry:view_own` | The caller's entries, newest first |
| `GET /api/work-entries/export?from=&to=&projectId=&userId=&clientId=&search=` | `work_entry:view_all` | The matching entries as a CSV timesheet (below) |
| `GET /api/work-entries/{id}` | `work_entry:view_all`, or `work_entry:view_own` for your own | The entry, its comments, and the same person's other entries on the project that day |

People log, edit, delete and restore only their **own** entries; anyone else's reads as `404`, whatever the caller may view. The rules are in `Business/WorkEntries/WorkEntryRules.cs`:

- **Projects:** Design only (`422 not_for_this_domain`). A project accepts work while it is active, or on hold when `holdLogging` is on (`422 project_not_accepting_work`). Completed and archived projects never do.
- **Times** are UTC instants in whole minutes; a time without an offset is read as UTC. An entry is 5 minutes to 16 hours long (`400`). It belongs to the company-time-zone day it starts on, and may run past midnight.
- **Future:** an entry may not end after now unless `allowFutureEntries` is on (`422 entry_in_future`).
- **Description:** required while `requireDescription` is on (`400 description_required`). At most 2000 characters.
- **Overlap:** a person's entries may not overlap, unless `allowOverlap` is on (`422 entry_overlaps`). Back-to-back entries are fine. Each change locks the person's user row, so two simultaneous changes are checked one after the other.
- **Changing an entry** (edit, delete, restore) is allowed within the edit window, counted from when it was logged (edits don't extend it), and only while its project still accepts work (`422 edit_window_closed`, `422 project_not_accepting_work`). Responses carry `canEdit` for the caller.
- **`moves`** saves the timeline's swap: other entries of the caller moved to make room, `[{ id, startedAt, endedAt }]` (at most 20). They are checked with the new entry and saved in the same transaction, or not at all.
- The timer runs in the client; stopping it logs an entry through `POST /api/work-entries`.
- Logging never adds the person to the project's members.

**Events:** `work_entry_logged`, `work_entry_updated` (also once per moved entry), `work_entry_deleted`, `work_entry_restored`, with the entry as the subject and its minutes in the payload.

**Notifications** go to the active people who see everyone's work in Design (`work_entry:view_all`), never the author, following `notifyEntry`:

| `notifyEntry` | Sends |
|---|---|
| `all` | One `work_logged` notification per entry (author, project, minutes and description in the payload) |
| `long` | The same, only for entries of 3 hours or more |
| `digest` | One `work_digest` notification per recipient per company day (digest key `work-digest-YYYY-MM-DD`). Each entry adds 1 to `count`, adds its minutes to the payload's `minutes`, and marks it unread again |
| `off` | Nothing |

**Reading:** `from` and `to` are company-time-zone days, both inclusive, matched on the start time. `search` matches the description or project title (every word). Each entry carries the person, the project, `minutes`, `commentCount` and `canEdit`.

**CSV export** (`work-entries-<from>-to-<to>.csv`): UTF-8 with a byte-order mark and CRLF line ends, oldest first, one row per entry. Columns: Date, Weekday, Start, End (company time zone, in the company's 12- or 24-hour format), Hours (2 decimals), Employee, Email, Team, Project, Client (`Internal` for internal projects), Task, then **Hourly rate, Currency, Labour cost** only for callers with `financial:view`, then Entry id. The rate is the person's hourly cost in force on the day worked, in its own currency (`ProjectFinance.RateOn`, the same rule as project cost); the cost is hours × rate, rounded to cents. The overtime column comes once the working-time engine exists. Exports use the `export` rate-limit policy: 10 a minute per user (`RateLimiting:ExportPermitsPerMinute`).

## My work and a person's summary

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/work-summary?from=&to=&projectId=&search=` | `work_entry:view_own` | The caller's totals and series (below) |
| `GET /api/employees/{userId}/work-summary?from=&to=&projectId=&search=` | `work_entry:view_all` | The same for someone else (their profile). A person of a domain the caller can't see reads as `404` |
| `GET /api/me/projects` | `work_entry:view_own` | Every active and on-hold Design project, with the caller's own time on it |
| `GET /api/me/projects/{id}/work` | `work_entry:view_own` | The caller's work on one project and the team's latest entries there |
| `GET /api/me/day?date=` | `work_entry:view_own` | One day (today by default): the caller's entries and the domain's meetings, earliest first, for the day timeline |

Everything is calculated from the entries on each request (`Business/WorkEntries/WorkStats.cs`); nothing is stored. An entry counts on the company-time-zone day it starts on, and weeks start on the company's week start (`company_settings.week_start`, Monday until the company settings exist).

- **Summary:** today, each day of this week, this week, the whole of last week, this month, this quarter, and last quarter up to the same number of days (for a fair comparison); all-time minutes, entries, first day and projects; active days and the longest run of consecutive active days in the last 365 days; the last 16 weeks; every active day of the last 365 days (for the heat map); and time per project. `from`, `to`, `projectId` and `search` narrow only the time per project.
- **My projects:** every open project, as the prototype shows them to employees (not only those they are members of): whether it accepts work, the caller's minutes and latest start, the people who logged on it (most time first, names only), and the latest activity. Other people's hours stay private. Projects the caller worked on come first, most recent first, then the newest.
- **My project work:** the caller's minutes, entries and minutes this week on the project; how many people logged there and the project's total minutes; the caller's 12 latest entries; the caller's last 8 weeks there; and the 6 latest entries by other people (name, start and description, as the prototype's "Team activity").
- The overtime figures of these pages come from the working-time engine once it exists.

## Comments

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/work-entries/{id}/comments` | Whoever can read the entry | Oldest first, with the author's name and role |
| `POST /api/work-entries/{id}/comments` | `work_entry:comment` | `{ body, sendToGroup }`. Returns `201` |

- A comment is 1 to 2000 characters. Comments can't be edited or deleted.
- `sendToGroup` (also post to the author's team group chat) is refused with `422 group_chat_unavailable` until group chats exist.
- The entry's author gets an `entry_comment` notification (commenter's name, the text and the entry's description), unless they wrote it themselves.
- Event: `work_entry_commented`, with the entry as the subject and the comment id in the payload.

## Meetings

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/meetings?projectId=&mine=&from=&to=` | `project:view` | Meetings of every project the caller can see, earliest first. `mine=true` keeps those the caller set; `from`/`to` are company days a meeting overlaps |
| `GET /api/projects/{id}/meetings` | `project:view` | A project's meetings, earliest first |
| `POST /api/projects/{id}/meetings` | `meeting:manage` | `{ startsAt, endsAt, topic, description }`. Returns `201` |
| `PUT /api/meetings/{id}` | `meeting:manage` | Moves, resizes or renames a meeting (the same body) |
| `DELETE /api/meetings/{id}` | `meeting:manage` | Soft delete (`deleted_at`). Returns `204` |

Rules (`Business/Meetings/MeetingRules.cs`):

- Meetings belong to Design projects (`422 not_for_this_domain`) that are active or on hold (`422 project_closed`).
- Times are UTC instants in whole minutes. A meeting lasts at least 5 minutes and starts and ends on the same company-time-zone day (`400`). Past days are allowed.
- Topic up to 200 characters, description up to 2000; both optional.
- A meeting has no attendees: it is a project-wide block. It is not work time and never counts in hours.
- Events: `meeting_created`, `meeting_updated`, `meeting_deleted`, with the meeting as the subject and its topic and times in the payload.

**Who manages meetings:** for now only holders of `meeting:manage` (Design admins and super admins by default). The prototype also lets employees set meetings and remove their own; that needs a new permission seeded for employees, which is a database change awaiting approval (business-rules-review.md, open question 4).
